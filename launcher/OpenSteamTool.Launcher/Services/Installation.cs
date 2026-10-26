using System.Security.Cryptography;

namespace OpenSteamTool.Launcher;

internal readonly record struct OpResult(bool Ok, string Message);

internal static class Installation
{
    /// <summary>必须部署到 Steam 根目录的三个产物。</summary>
    public static readonly string[] Files = { "OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll" };

    public static bool IsInstalled(string? steamRoot)
        => SteamLocator.IsValid(steamRoot)
           && File.Exists(Path.Combine(steamRoot!, "OpenSteamTool.dll"));

    public static string? LatestBackup(string steamRoot)
    {
        var root = Path.Combine(steamRoot, "opensteamtool", "backup");
        if (!Directory.Exists(root)) return null;
        return Directory.GetDirectories(root)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static OpResult Install(string? steamRoot, string? sourceDir, Action<string> log, bool autoCloseSteam)
    {
        if (!SteamLocator.IsValid(steamRoot))
            return new(false, "Steam 目录无效：未找到 steam.exe");
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            return new(false, "组件目录不存在，请先构建 OpenSteamTool 或选择 build\\Release");

        foreach (var file in Files)
        {
            if (!File.Exists(Path.Combine(sourceDir!, file)))
                return new(false, $"组件目录缺少 {file}");
        }

        if (SteamLocator.IsSteamRunning())
        {
            if (!autoCloseSteam)
                return new(false, "Steam 正在运行，请先退出（或勾选“安装前自动关闭 Steam”）");
            log("正在请求 Steam 退出…");
            if (!SteamLocator.Shutdown(steamRoot!, TimeSpan.FromSeconds(25)))
                return new(false, "Steam 未能自动退出，请手动关闭后重试");
            log("Steam 已退出");
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupDir = Path.Combine(steamRoot!, "opensteamtool", "backup", stamp);

        try
        {
            Directory.CreateDirectory(backupDir);
            foreach (var file in Files)
            {
                var target = Path.Combine(steamRoot!, file);
                if (File.Exists(target))
                {
                    File.Copy(target, Path.Combine(backupDir, file), true);
                    log($"已备份 {file}");
                }

                var source = Path.Combine(sourceDir!, file);
                File.Copy(source, target, true);

                if (!SameHash(source, target))
                    return new(false, $"{file} 复制后校验失败，请检查磁盘/权限");

                log($"已写入 {file}  ({new FileInfo(target).Length / 1024} KB)");
            }

            File.WriteAllText(Path.Combine(backupDir, "info.txt"),
                $"source={sourceDir}{Environment.NewLine}time={DateTime.Now:O}{Environment.NewLine}");
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "写入被拒绝：Steam 可能安装在受保护目录，请右键“以管理员身份运行”");
        }
        catch (IOException ex)
        {
            return new(false, "写入失败：" + ex.Message);
        }

        return new(true, $"安装完成（备份：{backupDir}）");
    }

    public static OpResult Uninstall(string? steamRoot, Action<string> log, bool autoCloseSteam)
    {
        if (!SteamLocator.IsValid(steamRoot))
            return new(false, "Steam 目录无效：未找到 steam.exe");

        if (SteamLocator.IsSteamRunning())
        {
            if (!autoCloseSteam)
                return new(false, "Steam 正在运行，请先退出（或勾选“安装前自动关闭 Steam”）");
            log("正在请求 Steam 退出…");
            if (!SteamLocator.Shutdown(steamRoot!, TimeSpan.FromSeconds(25)))
                return new(false, "Steam 未能自动退出，请手动关闭后重试");
            log("Steam 已退出");
        }

        var backup = LatestBackup(steamRoot!);
        log(backup is null ? "未找到备份，将直接删除已部署的文件" : $"使用备份：{backup}");

        try
        {
            foreach (var file in Files)
            {
                var target = Path.Combine(steamRoot!, file);
                var backupFile = backup is null ? null : Path.Combine(backup, file);

                if (backupFile is not null && File.Exists(backupFile))
                {
                    File.Copy(backupFile, target, true);
                    log($"已还原 {file}");
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                    log($"已删除 {file}");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "删除/还原被拒绝：请以管理员身份运行本程序");
        }
        catch (IOException ex)
        {
            return new(false, "卸载失败：" + ex.Message);
        }

        return new(true, "卸载完成");
    }

    public static string DescribeMetadata(string? steamRoot)
    {
        if (steamRoot is null) return "未设置 Steam 目录";
        var root = Path.Combine(steamRoot, "opensteamtool");
        if (!Directory.Exists(root)) return "尚无元数据缓存（安装后启动一次 Steam 生成）";

        int Count(string channel)
        {
            var dir = Path.Combine(root, channel);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.toml", SearchOption.AllDirectories).Length : 0;
        }

        return $"pattern 缓存 {Count("pattern")} 个 · ipc 缓存 {Count("ipc")} 个 · 目录 {root}";
    }

    private static bool SameHash(string a, string b)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return sha.ComputeHash(fa).SequenceEqual(sha.ComputeHash(fb));
        }
        catch
        {
            return false;
        }
    }
}
