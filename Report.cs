namespace WirelessMouseBatteryTray;

// A text file a friend can send when their mouse does not work: what the app sees for every present mouse.
internal static class Report
{
    public static IEnumerable<string> Lines()
    {
        yield return $"Wireless Mouse Battery Tray {typeof(Report).Assembly.GetName().Version?.ToString(3)}";
        yield return $"Windows {Environment.OSVersion.Version}";
        yield return $"created {DateTimeOffset.Now:yyyy-MM-dd HH:mm}";
        var mouse = MouseChoice.Load();
        yield return $"chosen mouse: {(mouse is null ? "none (automatic)" : $"{mouse.Name} ({mouse.DeviceKey})")}";
        yield return "";

        foreach (var target in BatteryReader.PresentMice())
        {
            foreach (var line in BatteryReader.Diagnose(target)) yield return line;
            yield return "";
        }

        yield return "last log lines:";
        foreach (var line in AppLog.Tail(80)) yield return line;
    }

    public static string WriteToDesktop()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Wireless Mouse Battery report.txt");
        File.WriteAllLines(path, Lines());
        return path;
    }
}
