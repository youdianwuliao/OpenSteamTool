using System.Diagnostics;
using Microsoft.Win32;

namespace OpenSteamTool.Launcher;

internal static class SteamLocator
{
    /// <summary>按注册表顺序探测 Steam 根目录，找不到返回 null。</summary>
    public static string? Detect()
    {
        var candidates = new List<string?>
        {
            Read(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            Read(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            Read(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            Read(Registry.CurrentUser, @"Software\Valve\Steam", "InstallPath")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var normalized = Normalize(candidate);
            if (IsValid(normalized)) return normalized;
        }
        return null;
    }

    private static string? Read(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    public static string Normalize(string path)
        => Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');

    public static bool IsValid(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path!, "steam.exe"));

    public static bool IsSteamRunning()
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

    /// <summary>通过 steam.exe -shutdown 优雅退出，并等待进程消失。</summary>
    public static bool Shutdown(string steamRoot, TimeSpan timeout)
    {
        try
        {
            var exe = Path.Combine(steamRoot, "steam.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe, "-shutdown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsSteamRunning()) return true;
            Thread.Sleep(400);
        }
        return !IsSteamRunning();
    }

    public static bool Launch(string steamRoot)
    {
        try
        {
            var exe = Path.Combine(steamRoot, "steam.exe");
            if (!File.Exists(exe)) return false;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
