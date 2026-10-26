using System.Runtime.InteropServices;

namespace OpenSteamTool.Launcher;

/// <summary>
/// 内置票据提取：把 steamclient64.dll 载入启动器进程，走和
/// tools/extract_tickets 相同的接口路径（SteamClient023 → CreateSteamPipe →
/// ConnectToGlobalUser → ISteamAppTicket / ISteamUser + ISteamUtils）。
///
/// 前提与 CLI 一致：Steam 正在运行、已登录，且当前账号真正拥有该 AppId。
/// trace 回调会逐步输出每个阶段，便于定位是哪一步失败。
/// </summary>
internal static class SteamTicketReader
{
    private const string SteamClientVersion = "SteamClient023";
    private const string SteamUserVersion = "SteamUser023";
    private const string SteamUtilsVersion = "SteamUtils010";
    private const string AppTicketInterfaceVersion = "STEAMAPPTICKET_INTERFACE_VERSION001";

    private const int EncryptedAppTicketCallback = 154; // k_iSteamUserCallbacks(100) + 54
    private const int TicketBufferSize = 4096;
    private const int PollStepMs = 50;
    private const int PollTimeoutMs = 15000;

    // ISteamClient vtable 槽位（顺序与 tools/extract_tickets/steam.h 一致）
    private const int SlotCreateSteamPipe = 0;
    private const int SlotReleaseSteamPipe = 1;
    private const int SlotConnectToGlobalUser = 2;
    private const int SlotGetISteamUser = 5;
    private const int SlotGetISteamUtils = 9;
    private const int SlotGetGenericInterface = 12;

    // ISteamUtils vtable
    private const int SlotIsApiCallCompleted = 11;
    private const int SlotGetApiCallResult = 13;

    // ISteamUser vtable
    private const int SlotRequestEncryptedAppTicket = 21;
    private const int SlotGetEncryptedAppTicket = 22;

    private const uint LoadWithAlteredSearchPath = 0x00000008;

    public static TicketResult Read(int appId, string? steamRoot, Action<string>? trace = null)
    {
        void Trace(string message) => trace?.Invoke(message);

        if (steamRoot is null) return new TicketResult(false, "未设置 Steam 目录", null, null);
        if (!Environment.Is64BitProcess) return new TicketResult(false, "启动器必须是 64 位进程", null, null);

        var dllPath = Path.Combine(steamRoot, "steamclient64.dll");
        if (!File.Exists(dllPath)) return new TicketResult(false, "找不到 " + dllPath, null, null);

        Trace($"加载 {dllPath}");

        IntPtr module = IntPtr.Zero;
        IntPtr ticketBuffer = IntPtr.Zero;
        IntPtr encryptedBuffer = IntPtr.Zero;
        IntPtr resultBuffer = IntPtr.Zero;

        try
        {
            // steamclient64.dll 依赖同目录的 tier0_s64 / vstdlib_s64，需要改搜索路径。
            SetDllDirectory(steamRoot);
            module = LoadLibraryEx(dllPath, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (module == IntPtr.Zero)
                return Fail($"LoadLibraryEx 失败（Win32 {Marshal.GetLastWin32Error()}）", Trace);

            Trace($"steamclient64.dll 句柄 0x{module.ToInt64():X}");

            var createInterfacePtr = GetProcAddress(module, "CreateInterface");
            if (createInterfacePtr == IntPtr.Zero) return Fail("导出表里没有 CreateInterface", Trace);

            var createInterface = Marshal.GetDelegateForFunctionPointer<CreateInterfaceFn>(createInterfacePtr);
            var client = createInterface(SteamClientVersion, out int returnCode);
            if (client == IntPtr.Zero)
                return Fail($"CreateInterface({SteamClientVersion}) 返回空（code={returnCode}）", Trace);

            Trace($"CreateInterface({SteamClientVersion}) → 0x{client.ToInt64():X}");

            var pipe = Vtable<CreateSteamPipeFn>(client, SlotCreateSteamPipe)(client);
            if (pipe == 0) return Fail("CreateSteamPipe 返回 0：Steam 是否在运行？", Trace);

            Trace($"CreateSteamPipe → {pipe}");

            try
            {
                var user = Vtable<ConnectToGlobalUserFn>(client, SlotConnectToGlobalUser)(client, pipe);
                if (user == 0) return Fail("ConnectToGlobalUser 返回 0：Steam 是否已登录？", Trace);

                Trace($"ConnectToGlobalUser → {user}");

                var appTicket = ReadOwnershipTicket(client, pipe, user, appId, ref ticketBuffer, Trace);
                var eTicket = ReadEncryptedTicket(client, pipe, user, ref encryptedBuffer, ref resultBuffer, Trace,
                                                  out var encryptedMessage);

                if (appTicket is null && eTicket is null)
                    return Fail("未取到任何票据：" + encryptedMessage, Trace);

                Trace($"完成 AppTicket {Describe(appTicket)} / ETicket {Describe(eTicket)}");
                return new TicketResult(true,
                    $"内置提取成功：AppTicket {Describe(appTicket)} / ETicket {Describe(eTicket)}",
                    appTicket, eTicket);
            }
            finally
            {
                Vtable<ReleaseSteamPipeFn>(client, SlotReleaseSteamPipe)(client, pipe);
                Trace($"BReleaseSteamPipe({pipe})");
            }
        }
        catch (Exception ex)
        {
            return Fail("异常：" + ex.GetType().Name + " " + ex.Message, Trace);
        }
        finally
        {
            // 清理阶段绝不能让异常逃逸，否则会掩盖上面已经得出的失败原因。
            try
            {
                SetDllDirectory(null);
                if (ticketBuffer != IntPtr.Zero) Marshal.FreeHGlobal(ticketBuffer);
                if (encryptedBuffer != IntPtr.Zero) Marshal.FreeHGlobal(encryptedBuffer);
                if (resultBuffer != IntPtr.Zero) Marshal.FreeHGlobal(resultBuffer);
                if (module != IntPtr.Zero) FreeLibrary(module);
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    private static string? ReadOwnershipTicket(IntPtr client, int pipe, int user, int appId,
                                               ref IntPtr buffer, Action<string> trace)
    {
        var generic = Vtable<GetGenericInterfaceFn>(client, SlotGetGenericInterface);
        var appTicketInterface = generic(client, user, pipe, AppTicketInterfaceVersion);
        if (appTicketInterface == IntPtr.Zero)
        {
            trace($"{AppTicketInterfaceVersion} 接口为空（AppTicket 跳过）");
            return null;
        }

        trace($"{AppTicketInterfaceVersion} → 0x{appTicketInterface.ToInt64():X}");

        buffer = Marshal.AllocHGlobal(TicketBufferSize);
        var getTicket = Vtable<GetAppOwnershipTicketDataFn>(appTicketInterface, 0);
        uint written = getTicket(appTicketInterface, (uint)appId, buffer, TicketBufferSize,
                                 out _, out _, out _, out _);

        if (written == 0 || written > TicketBufferSize)
        {
            trace($"GetAppOwnershipTicketData 返回 {written} 字节（该账号可能不拥有 AppId {appId}）");
            return null;
        }

        trace($"AppTicket {written} 字节");
        return ToHex(buffer, (int)written);
    }

    private static string? ReadEncryptedTicket(IntPtr client, int pipe, int user,
                                               ref IntPtr buffer, ref IntPtr resultBuffer,
                                               Action<string> trace, out string message)
    {
        message = "";

        var steamUser = Vtable<GetISteamUserFn>(client, SlotGetISteamUser)(client, user, pipe, SteamUserVersion);
        var steamUtils = Vtable<GetISteamUtilsFn>(client, SlotGetISteamUtils)(client, pipe, SteamUtilsVersion);
        if (steamUser == IntPtr.Zero || steamUtils == IntPtr.Zero)
        {
            message = $"GetISteamUser/GetISteamUtils 返回空（user=0x{steamUser.ToInt64():X} utils=0x{steamUtils.ToInt64():X}）";
            trace(message);
            return null;
        }

        trace($"ISteamUser 0x{steamUser.ToInt64():X} · ISteamUtils 0x{steamUtils.ToInt64():X}");

        var requestTicket = Vtable<RequestEncryptedAppTicketFn>(steamUser, SlotRequestEncryptedAppTicket);
        ulong call = requestTicket(steamUser, IntPtr.Zero, 0);
        if (call == 0)
        {
            message = "RequestEncryptedAppTicket 返回 0";
            trace(message);
            return null;
        }

        trace($"RequestEncryptedAppTicket → hCall=0x{call:X}，等待回调…");

        var isCompleted = Vtable<IsApiCallCompletedFn>(steamUtils, SlotIsApiCallCompleted);
        var deadline = DateTime.UtcNow.AddMilliseconds(PollTimeoutMs);
        bool completed = false;

        while (DateTime.UtcNow < deadline)
        {
            if (isCompleted(steamUtils, call, out byte failed) != 0)
            {
                completed = true;
                if (failed != 0)
                {
                    message = "EncryptedAppTicketResponse 标记失败";
                    trace(message);
                    return null;
                }
                break;
            }
            Thread.Sleep(PollStepMs);
        }

        if (!completed)
        {
            message = $"等待 EncryptedAppTicketResponse 超时（{PollTimeoutMs} ms）";
            trace(message);
            return null;
        }

        resultBuffer = Marshal.AllocHGlobal(4);
        var getResult = Vtable<GetApiCallResultFn>(steamUtils, SlotGetApiCallResult);
        if (getResult(steamUtils, call, resultBuffer, 4, EncryptedAppTicketCallback, out _) == 0)
        {
            message = "GetAPICallResult 读取回调失败";
            trace(message);
            return null;
        }

        int result = Marshal.ReadInt32(resultBuffer);
        if (result != 1) // k_EResultOK
        {
            message = $"EncryptedAppTicketResponse 结果码 {result}";
            trace(message);
            return null;
        }

        buffer = Marshal.AllocHGlobal(TicketBufferSize);
        var getTicket = Vtable<GetEncryptedAppTicketFn>(steamUser, SlotGetEncryptedAppTicket);
        if (getTicket(steamUser, buffer, TicketBufferSize, out uint written) == 0
            || written == 0 || written > TicketBufferSize)
        {
            message = "GetEncryptedAppTicket 未返回数据";
            trace(message);
            return null;
        }

        trace($"ETicket {written} 字节");
        return ToHex(buffer, (int)written);
    }

    private static TicketResult Fail(string message, Action<string> trace)
    {
        trace("失败：" + message);
        return new TicketResult(false, message, null, null);
    }

    private static T Vtable<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var entry = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(entry);
    }

    private static string ToHex(IntPtr buffer, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(buffer, bytes, 0, length);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Describe(string? hex)
        => hex is null ? "无" : hex.Length / 2 + " 字节";

    // ── native imports ────────────────────────────────────────────────

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? path);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    // ── interface delegates（C++ bool 按 1 字节处理，避免默认 4 字节 BOOL 错位）──

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateInterfaceFn(
        [MarshalAs(UnmanagedType.LPStr)] string name, out int returnCode);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateSteamPipeFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte ReleaseSteamPipeFn(IntPtr self, int pipe);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ConnectToGlobalUserFn(IntPtr self, int pipe);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetISteamUserFn(
        IntPtr self, int user, int pipe, [MarshalAs(UnmanagedType.LPStr)] string version);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetISteamUtilsFn(
        IntPtr self, int pipe, [MarshalAs(UnmanagedType.LPStr)] string version);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetGenericInterfaceFn(
        IntPtr self, int user, int pipe, [MarshalAs(UnmanagedType.LPStr)] string version);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetAppOwnershipTicketDataFn(
        IntPtr self, uint appId, IntPtr buffer, uint bufferLength,
        out uint appIdOffset, out uint steamIdOffset, out uint signatureOffset, out uint signatureSize);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong RequestEncryptedAppTicketFn(IntPtr self, IntPtr data, int dataLength);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte IsApiCallCompletedFn(IntPtr self, ulong call, out byte failed);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte GetApiCallResultFn(
        IntPtr self, ulong call, IntPtr callback, int callbackSize, int expectedCallback, out byte failed);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte GetEncryptedAppTicketFn(
        IntPtr self, IntPtr ticket, int maxTicket, out uint written);
}
