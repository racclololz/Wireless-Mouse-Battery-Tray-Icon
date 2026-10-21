namespace WirelessMouseBatteryTray;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WirelessMouseBatteryTray");

    private static string LogPath => Path.Combine(Folder, "battery.log");

    internal static IEnumerable<string> Tail(int count)
    {
        try
        {
            lock (Gate) return File.Exists(LogPath) ? File.ReadLines(LogPath).TakeLast(count).ToList() : [];
        }
        catch
        {
            return [];
        }
    }

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var path = LogPath;
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
