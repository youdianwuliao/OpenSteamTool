using System.Diagnostics;
using System.Text;
using OpenSteamTool.Launcher;

namespace OpenSteamTool.Probe;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("用法: ost-ticket-probe <AppId> [SteamRoot]");
            Console.WriteLine("示例: ost-ticket-probe 1361510 \"C:\\Program Files (x86)\\Steam\"");
            Console.WriteLine("      ost-ticket-probe 1361510    (自动按 Program Files 探测)");
            return 1;
        }

        if (!int.TryParse(args[0], out int appId) || appId <= 0)
        {
            Console.WriteLine("AppId 必须是正整数。");
            return 1;
        }

        var steamRoot = args.Length > 1 ? Path.GetFullPath(args[1]) : GuessSteamRoot();
        if (steamRoot is null)
        {
            Console.WriteLine("找不到 Steam 目录，请把路径作为第二个参数传入。");
            return 2;
        }

        Console.WriteLine(new string('=', 66));
        Console.WriteLine($" Steam 目录 : {steamRoot}");
        Console.WriteLine($" steam.exe  : {File.Exists(Path.Combine(steamRoot, "steam.exe"))}");
        Console.WriteLine($" Steam 运行 : {IsSteamRunning()}");
        Console.WriteLine($" 进程位数   : {(Environment.Is64BitProcess ? "x64" : "x86（内置提取不可用）")}");
        Console.WriteLine($" AppId      : {appId}");
        Console.WriteLine(new string('=', 66));

        var stopwatch = Stopwatch.StartNew();
        var result = SteamTicketReader.Read(appId, steamRoot, stage => Console.WriteLine("  · " + stage));
        stopwatch.Stop();

        Console.WriteLine(new string('-', 66));
        Console.WriteLine($" 耗时     : {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($" 结果     : {(result.Ok ? "成功" : "失败")} — {result.Message}");

        if (!string.IsNullOrEmpty(result.AppTicket))
            Console.WriteLine($" AppTicket: {result.AppTicket.Length / 2} 字节 · {Prefix(result.AppTicket!)}");

        if (!string.IsNullOrEmpty(result.ETicket))
            Console.WriteLine($" ETicket  : {result.ETicket.Length / 2} 字节 · {Prefix(result.ETicket!)}");

        Console.WriteLine(new string('=', 66));
        return result.Ok ? 0 : 3;
    }

    private static string Prefix(string hex) => hex.Length <= 32 ? hex : hex[..32] + "…";

    private static bool IsSteamRunning()
    {
        try
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GuessSteamRoot()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("STEAM_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (File.Exists(Path.Combine(candidate!, "steam.exe"))) return candidate;
        }
        return null;
    }
}
