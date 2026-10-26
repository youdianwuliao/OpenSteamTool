namespace OpenSteamTool.Launcher;

internal static class LogFiles
{
    public static string[] List(string? steamRoot)
    {
        if (steamRoot is null) return Array.Empty<string>();
        var dir = Path.Combine(steamRoot, "opensteamtool");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        var files = Directory.GetFiles(dir, "*.log", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static string ReadTail(string path, int maxBytes = 256 * 1024)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            var take = (int)Math.Min(length, maxBytes);
            stream.Seek(length - take, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            return "[读取失败] " + ex.Message;
        }
    }
}
