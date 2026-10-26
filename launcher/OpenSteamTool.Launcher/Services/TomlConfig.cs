using System.Text;

namespace OpenSteamTool.Launcher;

/// <summary>
/// 行级 TOML 编辑器：只改动 GUI 认识的键，保留用户原有的注释、顺序和未知键。
/// </summary>
internal sealed class TomlConfig
{
    private readonly List<string> _lines;

    public string FilePath { get; }

    private TomlConfig(string path, IEnumerable<string> lines)
    {
        FilePath = path;
        _lines = new List<string>(lines);
    }

    public static TomlConfig Load(string path)
        => new(path, File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>());

    public bool Exists => File.Exists(FilePath);

    public string? GetString(string section, string key)
    {
        var raw = GetRaw(section, key);
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return trimmed;
    }

    public string? GetRaw(string section, string key)
    {
        if (!TryFindKey(section, key, out int index, out _)) return null;
        return ValuePart(_lines[index]).Trim();
    }

    public int GetInt(string section, string key, int fallback)
        => int.TryParse(GetRaw(section, key), out int value) ? value : fallback;

    public bool GetBool(string section, string key, bool fallback)
    {
        var raw = GetRaw(section, key);
        if (raw is null) return fallback;
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public List<string> GetStringArray(string section, string key)
    {
        var result = new List<string>();
        if (!TryFindKey(section, key, out int index, out int endOfValue)) return result;

        var joined = new StringBuilder(ValuePart(_lines[index])).Append(' ');
        // 续行没有 "="，必须整行拼接，否则多行数组会丢内容。
        for (int i = index + 1; i <= endOfValue; i++) joined.Append(_lines[i]).Append(' ');

        var text = joined.ToString();
        int position = 0;
        while (position < text.Length)
        {
            int open = text.IndexOf('"', position);
            if (open < 0) break;
            var value = new StringBuilder();
            int cursor = open + 1;
            while (cursor < text.Length)
            {
                char ch = text[cursor];
                if (ch == '\\' && cursor + 1 < text.Length)
                {
                    value.Append(text[cursor + 1]);
                    cursor += 2;
                    continue;
                }
                if (ch == '"') break;
                value.Append(ch);
                cursor++;
            }
            result.Add(value.ToString());
            position = cursor + 1;
        }
        return result;
    }

    public void SetString(string section, string key, string value)
        => SetRaw(section, key, "\"" + Escape(value) + "\"");

    public void SetInt(string section, string key, int value)
        => SetRaw(section, key, value.ToString());

    public void SetBool(string section, string key, bool value)
        => SetRaw(section, key, value ? "true" : "false");

    public void SetStringArray(string section, string key, IEnumerable<string> values)
    {
        var items = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => "\"" + Escape(v) + "\"");
        SetRaw(section, key, "[" + string.Join(", ", items) + "]");
    }

    public void SetRaw(string section, string key, string value)
    {
        if (TryFindKey(section, key, out int index, out int endOfValue))
        {
            _lines[index] = key + " = " + value;
            if (endOfValue > index) _lines.RemoveRange(index + 1, endOfValue - index);
            return;
        }

        var range = SectionRange(section);
        if (range.Start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add("[" + section + "]");
            _lines.Add(key + " = " + value);
            return;
        }

        int insertAt = range.End;
        while (insertAt > range.Start + 1 && _lines[insertAt - 1].Trim().Length == 0) insertAt--;
        _lines.Insert(insertAt, key + " = " + value);
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, string.Join(Environment.NewLine, _lines) + Environment.NewLine, new UTF8Encoding(false));
    }

    private (int Start, int End) SectionRange(string section)
    {
        var header = "[" + section + "]";
        int start = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        }
        if (start < 0) return (-1, -1);

        int end = _lines.Count;
        for (int i = start + 1; i < _lines.Count; i++)
        {
            if (_lines[i].TrimStart().StartsWith('[')) { end = i; break; }
        }
        return (start, end);
    }

    private bool TryFindKey(string section, string key, out int index, out int endOfValue)
    {
        index = -1;
        endOfValue = -1;

        var range = SectionRange(section);
        if (range.Start < 0) return false;

        for (int i = range.Start + 1; i < range.End; i++)
        {
            var line = _lines[i];
            int equals = line.IndexOf('=');
            if (equals < 0) continue;
            if (!line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            index = i;
            int balance = BracketBalance(line[equals..]);
            int cursor = i;
            while (balance > 0 && cursor + 1 < range.End)
            {
                cursor++;
                balance += BracketBalance(_lines[cursor]);
            }
            endOfValue = cursor;
            return true;
        }
        return false;
    }

    private static string ValuePart(string line)
    {
        int equals = line.IndexOf('=');
        return equals < 0 ? string.Empty : line[(equals + 1)..];
    }

    private static int BracketBalance(string text)
    {
        int balance = 0;
        foreach (char ch in text)
        {
            if (ch == '[') balance++;
            else if (ch == ']') balance--;
        }
        return balance;
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
