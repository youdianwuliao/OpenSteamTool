using System.Text;
using System.Text.RegularExpressions;

namespace OpenSteamTool.Launcher;

internal sealed class GameEntry
{
    public int AppId { get; set; }
    public string? DepotKey { get; set; }
    public string? Token { get; set; }
    public string? AppTicket { get; set; }
    public string? ETicket { get; set; }
    public string? Stat { get; set; }
    public string SourceFile { get; set; } = "";
    public bool Managed { get; set; }

    public bool HasTicket => !string.IsNullOrEmpty(AppTicket) || !string.IsNullOrEmpty(ETicket);

    public GameEntry Clone() => (GameEntry)MemberwiseClone();
}

internal sealed class ManifestPin
{
    public uint DepotId { get; set; }
    public ulong Gid { get; set; }
    public ulong Size { get; set; }
    public string SourceFile { get; set; } = "";
    public bool Managed { get; set; }

    public ManifestPin Clone() => (ManifestPin)MemberwiseClone();
}

internal sealed class LuaSnapshot
{
    public List<GameEntry> Games { get; } = new();
    public List<ManifestPin> Pins { get; } = new();
}

/// <summary>
/// 读取 <c>&lt;Steam&gt;/config/lua/*.lua</c> 里的函数调用，并把 GUI 的改动
/// 统一写回由本工具独占的 <c>opensteamtool_gui.lua</c>，绝不修改用户已有文件。
/// </summary>
internal static class LuaStore
{
    public const string ManagedFileName = "opensteamtool_gui.lua";

    private static readonly Regex AddAppIdPattern = new(
        @"addappid\s*\(\s*(\d+)\s*(?:,\s*\d+\s*(?:,\s*""([0-9A-Fa-f]{64})""\s*)?)?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AddTokenPattern = new(
        @"addtoken\s*\(\s*(\d+)\s*,\s*""([^""]*)""\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SetManifestPattern = new(
        @"setmanifestid\s*\(\s*(\d+)\s*,\s*""(\d+)""\s*(?:,\s*(\d+)\s*)?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SetAppTicketPattern = new(
        @"setappticket\s*\(\s*(\d+)\s*,\s*""([0-9A-Fa-f]*)""\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SetETicketPattern = new(
        @"seteticket\s*\(\s*(\d+)\s*,\s*""([0-9A-Fa-f]*)""\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SetStatPattern = new(
        @"setstat\s*\(\s*(\d+)\s*,\s*""(\d+)""\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LuaSnapshot Load(string? luaDir)
    {
        var snapshot = new LuaSnapshot();
        if (luaDir is null || !Directory.Exists(luaDir)) return snapshot;

        var files = Directory.GetFiles(luaDir, "*.lua", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            bool managed = string.Equals(Path.GetFileName(file), ManagedFileName, StringComparison.OrdinalIgnoreCase);

            foreach (Match m in AddAppIdPattern.Matches(text))
            {
                if (IsCommented(text, m.Index)) continue;
                if (!int.TryParse(m.Groups[1].Value, out int appId)) continue;
                var entry = GetOrAddGame(snapshot, appId, file, managed);
                if (m.Groups[2].Success && m.Groups[2].Value.Length > 0) entry.DepotKey = m.Groups[2].Value;
            }

            foreach (Match m in AddTokenPattern.Matches(text))
            {
                if (IsCommented(text, m.Index)) continue;
                if (!int.TryParse(m.Groups[1].Value, out int appId)) continue;
                GetOrAddGame(snapshot, appId, file, managed).Token = m.Groups[2].Value;
            }

            foreach (Match m in SetAppTicketPattern.Matches(text))
            {
                if (IsCommented(text, m.Index)) continue;
                if (!int.TryParse(m.Groups[1].Value, out int appId)) continue;
                GetOrAddGame(snapshot, appId, file, managed).AppTicket = m.Groups[2].Value;
            }

            foreach (Match m in SetETicketPattern.Matches(text))
            {
                if (IsCommented(text, m.Index)) continue;
                if (!int.TryParse(m.Groups[1].Value, out int appId)) continue;
                GetOrAddGame(snapshot, appId, file, managed).ETicket = m.Groups[2].Value;
            }

            foreach (Match m in SetStatPattern.Matches(text))
            {
                if (IsCommented(text, m.Index)) continue;
                if (!int.TryParse(m.Groups[1].Value, out int appId)) continue;
                GetOrAddGame(snapshot, appId, file, managed).Stat = m.Groups[2].Value;
            }

            foreach (Match m in SetManifestPattern.Matches(text))
            {
                if (IsCommented(text, m.Index)) continue;
                if (!uint.TryParse(m.Groups[1].Value, out uint depotId)) continue;
                if (!ulong.TryParse(m.Groups[2].Value, out ulong gid)) continue;

                var pin = snapshot.Pins.FirstOrDefault(p => p.DepotId == depotId);
                if (pin is null)
                {
                    pin = new ManifestPin { DepotId = depotId, SourceFile = Path.GetFileName(file), Managed = managed };
                    snapshot.Pins.Add(pin);
                }
                else if (managed)
                {
                    pin.Managed = true;
                    pin.SourceFile = Path.GetFileName(file);
                }

                pin.Gid = gid;
                pin.Size = m.Groups[3].Success && ulong.TryParse(m.Groups[3].Value, out ulong size) ? size : 0;
            }
        }

        snapshot.Games.Sort((a, b) => a.AppId.CompareTo(b.AppId));
        snapshot.Pins.Sort((a, b) => a.DepotId.CompareTo(b.DepotId));
        return snapshot;
    }

    public static string SaveManaged(string luaDir, IEnumerable<GameEntry> games, IEnumerable<ManifestPin> pins)
    {
        Directory.CreateDirectory(luaDir);
        var sb = new StringBuilder();
        sb.AppendLine("-- 由 OpenSteamTool Launcher 生成，请通过界面修改。");
        sb.AppendLine("-- generated by OpenSteamTool Launcher - manual edits are overwritten on save.");
        sb.AppendLine();

        foreach (var game in games.OrderBy(g => g.AppId))
        {
            sb.Append("addappid(").Append(game.AppId);
            if (!string.IsNullOrWhiteSpace(game.DepotKey))
                sb.Append(", 0, \"").Append(game.DepotKey).Append('"');
            sb.AppendLine(")");

            if (!string.IsNullOrWhiteSpace(game.Token))
                sb.AppendLine($"addtoken({game.AppId}, \"{game.Token}\")");
            if (!string.IsNullOrWhiteSpace(game.AppTicket))
                sb.AppendLine($"setAppTicket({game.AppId}, \"{game.AppTicket}\")");
            if (!string.IsNullOrWhiteSpace(game.ETicket))
                sb.AppendLine($"setETicket({game.AppId}, \"{game.ETicket}\")");
            if (!string.IsNullOrWhiteSpace(game.Stat))
                sb.AppendLine($"setStat({game.AppId}, \"{game.Stat}\")");

            sb.AppendLine();
        }

        foreach (var pin in pins.OrderBy(p => p.DepotId))
        {
            sb.Append($"setManifestid({pin.DepotId}, \"{pin.Gid}\"");
            if (pin.Size > 0) sb.Append($", {pin.Size}");
            sb.AppendLine(")");
        }

        var path = Path.Combine(luaDir, ManagedFileName);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static GameEntry GetOrAddGame(LuaSnapshot snapshot, int appId, string file, bool managed)
    {
        var entry = snapshot.Games.FirstOrDefault(g => g.AppId == appId);
        if (entry is null)
        {
            entry = new GameEntry { AppId = appId, SourceFile = Path.GetFileName(file), Managed = managed };
            snapshot.Games.Add(entry);
        }
        else if (managed)
        {
            entry.Managed = true;
            entry.SourceFile = Path.GetFileName(file);
        }
        return entry;
    }

    /// <summary>跳过被 Lua 单行注释掉的调用。</summary>
    private static bool IsCommented(string text, int index)
    {
        int lineStart = index <= 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
        if (lineStart >= index) return false;
        return text.AsSpan(lineStart, index - lineStart).IndexOf("--") >= 0;
    }
}
