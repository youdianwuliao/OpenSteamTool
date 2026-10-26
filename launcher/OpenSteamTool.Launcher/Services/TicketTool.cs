using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OpenSteamTool.Launcher;

internal sealed record TicketResult(bool Ok, string Message, string? AppTicket, string? ETicket);

internal static class TicketTool
{
    public const string ExecutableName = "extract_tickets.exe";

    /// <summary>从组件目录 / 当前目录向上查找 <c>tools/&lt;Config&gt;/extract_tickets.exe</c>。</summary>
    public static string? Find(string? sourceDir)
    {
        var starts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceDir)) starts.Add(sourceDir!);
        starts.Add(AppContext.BaseDirectory);
        starts.Add(Directory.GetCurrentDirectory());

        foreach (var start in starts)
        {
            DirectoryInfo? dir = new(start);
            for (int depth = 0; depth < 7 && dir is not null; depth++, dir = dir.Parent)
            {
                foreach (var candidate in new[]
                         {
                             Path.Combine(dir.FullName, "tools", "Release", ExecutableName),
                             Path.Combine(dir.FullName, "tools", "Debug", ExecutableName),
                             Path.Combine(dir.FullName, ExecutableName)
                         })
                {
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    public static TicketResult Extract(string exePath, int appId)
    {
        if (!File.Exists(exePath))
            return new TicketResult(false, "未找到 extract_tickets.exe", null, null);

        string stdout;
        string stderr;
        try
        {
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            };
            startInfo.ArgumentList.Add(appId.ToString());

            using var process = Process.Start(startInfo);
            if (process is null) return new TicketResult(false, "无法启动提取工具", null, null);

            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(true); } catch { }
                return new TicketResult(false, "提取超时（120 秒）", null, null);
            }
        }
        catch (Exception ex)
        {
            return new TicketResult(false, "启动失败：" + ex.Message, null, null);
        }

        var ticketFile = new[]
        {
            Path.Combine(Path.GetDirectoryName(exePath)!, appId.ToString(), "tickets.txt"),
            Path.Combine(Directory.GetCurrentDirectory(), appId.ToString(), "tickets.txt")
        }.FirstOrDefault(File.Exists);

        if (ticketFile is null)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            return new TicketResult(false, "未生成 tickets.txt（Steam 需运行且账号真正拥有该游戏）" +
                                            (string.IsNullOrEmpty(detail) ? "" : "：" + detail), null, null);
        }

        string content;
        try
        {
            content = File.ReadAllText(ticketFile);
        }
        catch (Exception ex)
        {
            return new TicketResult(false, "读取 tickets.txt 失败：" + ex.Message, null, null);
        }

        var appTicket = MatchTicket(content, "appticket");
        var eTicket = MatchTicket(content, "eticket");

        if (appTicket is null && eTicket is null)
            return new TicketResult(false, "票据为空：确认当前登录账号真正拥有该 AppId", null, null);

        return new TicketResult(true, $"已提取 AppTicket {Describe(appTicket)} / ETicket {Describe(eTicket)}", appTicket, eTicket);
    }

    private static string? MatchTicket(string content, string name)
    {
        var match = Regex.Match(content, @"^" + name + @"\((\d+) bytes\):([0-9A-Fa-f]+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[2].Value : null;
    }

    private static string Describe(string? hex)
        => hex is null ? "无" : hex.Length / 2 + " 字节";
}
