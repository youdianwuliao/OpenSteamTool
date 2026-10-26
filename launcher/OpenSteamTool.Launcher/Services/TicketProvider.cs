using System.Net.Http;
using System.Text.Json;

namespace OpenSteamTool.Launcher;

/// <summary>
/// 票据获取顺序：远程票据接口 → 内置提取（进程内调 steamclient64.dll）
/// → extract_tickets.exe 命令行工具。三者都要求当前账号真正拥有该 AppId。
/// </summary>
internal static class TicketProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static TicketResult Acquire(int appId, string? steamRoot, string? sourceDir, string? apiTemplate)
    {
        var messages = new List<string>();

        if (!string.IsNullOrWhiteSpace(apiTemplate))
        {
            var remote = TryRemote(appId, apiTemplate!.Trim());
            if (remote.Ok) return remote;
            messages.Add(remote.Message);
        }
        else
        {
            messages.Add("未配置票据接口");
        }

        // 内置提取：不需要 extract_tickets.exe，直接调 steamclient64.dll。
        if (steamRoot is not null && SteamLocator.IsSteamRunning())
        {
            var builtIn = SteamTicketReader.Read(appId, steamRoot);
            if (builtIn.Ok) return builtIn;
            messages.Add("内置提取：" + builtIn.Message);
        }
        else
        {
            messages.Add("Steam 未运行，跳过内置提取");
        }

        var tool = TicketTool.Find(sourceDir);
        if (tool is null)
        {
            messages.Add("未找到 extract_tickets.exe");
            return new TicketResult(false, string.Join("；", messages), null, null);
        }

        var local = TicketTool.Extract(tool, appId);
        if (local.Ok) return local;

        messages.Add(local.Message);
        return new TicketResult(false, string.Join("；", messages), null, null);
    }

    /// <summary>
    /// URL 模板支持 {appid} 占位符。响应可以是 JSON（appticket / eticket，
    /// 大小写不敏感），也可以是 key=value 行文本。
    /// </summary>
    public static TicketResult TryRemote(int appId, string template)
    {
        string url;
        try
        {
            url = template.Replace("{appid}", appId.ToString(), StringComparison.OrdinalIgnoreCase);
            var body = Http.GetStringAsync(url).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(body))
                return new TicketResult(false, "票据接口返回空内容", null, null);

            var (appTicket, eTicket) = Parse(body);
            if (appTicket is null && eTicket is null)
                return new TicketResult(false, "票据接口响应里没有 appticket / eticket 字段", null, null);

            return new TicketResult(true,
                $"远程票据 AppTicket {Describe(appTicket)} / ETicket {Describe(eTicket)}",
                appTicket, eTicket);
        }
        catch (Exception ex)
        {
            return new TicketResult(false, "票据接口失败：" + ex.Message, null, null);
        }
    }

    private static (string? AppTicket, string? ETicket) Parse(string body)
    {
        var trimmed = body.TrimStart();

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                return (PickJson(doc.RootElement, "appticket", "app_ticket", "AppTicket"),
                        PickJson(doc.RootElement, "eticket", "e_ticket", "ETicket"));
            }
            catch (JsonException)
            {
                // 落到 key=value 解析
            }
        }

        string? appTicket = null;
        string? eTicket = null;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            int equals = line.IndexOf('=');
            if (equals <= 0 && !line.Contains(':')) continue;

            string key;
            string value;
            if (equals > 0)
            {
                key = line[..equals].Trim();
                value = line[(equals + 1)..].Trim();
            }
            else
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                key = line[..colon].Trim();
                value = line[(colon + 1)..].Trim();
            }

            if (!IsHex(value)) continue;
            if (key.Equals("appticket", StringComparison.OrdinalIgnoreCase)) appTicket = value;
            else if (key.Equals("eticket", StringComparison.OrdinalIgnoreCase)) eTicket = value;
        }
        return (appTicket, eTicket);
    }

    private static string? PickJson(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (value is not null && IsHex(value.Trim())) return value.Trim();
            }
        }
        return null;
    }

    private static bool IsHex(string value)
        => value.Length > 1
           && value.Length % 2 == 0
           && value.All(ch => Uri.IsHexDigit(ch));

    private static string Describe(string? hex)
        => hex is null ? "无" : hex.Length / 2 + " 字节";
}
