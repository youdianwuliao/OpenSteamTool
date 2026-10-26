using System.Text.Json;

namespace OpenSteamTool.Launcher;

internal sealed class Settings
{
    public string? SteamRoot { get; set; }
    public string? SourceDir { get; set; }
    public bool AutoCloseSteam { get; set; }
    public int LastPage { get; set; }

    /// <summary>可选：远程票据接口，支持 {appid} 占位符。</summary>
    public string? TicketApiTemplate { get; set; }

    /// <summary>交给 Steam 下载前自动准备（AppTicket / ETicket）。</summary>
    public bool AutoPrepareTickets { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSteamTool.Launcher",
        "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // 配置坏了就用默认值，不阻塞启动。
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 忽略：设置写入失败不影响主流程。
        }
    }
}

internal static class AppState
{
    public static Settings Settings { get; } = Settings.Load();

    public static string? SteamRoot
        => string.IsNullOrWhiteSpace(Settings.SteamRoot) ? null : Settings.SteamRoot;

    public static string? LuaDir
        => SteamRoot is null ? null : Path.Combine(SteamRoot, "config", "lua");

    public static string? ConfigPath
        => SteamRoot is null ? null : Path.Combine(SteamRoot, "opensteamtool.toml");

    public static string? LogDir
        => SteamRoot is null ? null : Path.Combine(SteamRoot, "opensteamtool");

    public static string? SourceDir
        => string.IsNullOrWhiteSpace(Settings.SourceDir) ? null : Settings.SourceDir;

    /// <summary>从当前 exe / 工作目录向上查找 CMake 构建产物目录。</summary>
    public static string? ProbeSourceDir()
    {
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts)
        {
            DirectoryInfo? dir = new(start);
            for (int depth = 0; depth < 7 && dir is not null; depth++, dir = dir.Parent)
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(dir.FullName, "build", config);
                    if (File.Exists(Path.Combine(candidate, "OpenSteamTool.dll"))) return candidate;
                }
                if (File.Exists(Path.Combine(dir.FullName, "OpenSteamTool.dll"))
                    && File.Exists(Path.Combine(dir.FullName, "dwmapi.dll")))
                {
                    return dir.FullName;
                }
            }
        }
        return null;
    }
}
