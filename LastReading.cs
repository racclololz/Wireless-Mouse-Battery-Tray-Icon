using System.Text.Json;

namespace WirelessMouseBatteryTray;

// Last fresh value on disk, so a restart while the mouse sleeps shows grey "last 60%" instead of "?".
internal sealed record LastReading(int Percent, DateTime TimeUtc, string? Name)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WirelessMouseBatteryTray", "last.json");

    public static LastReading? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<LastReading>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Losing the cached value only costs the grey number after a restart.
        }
    }

    public static void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
        }
    }
}
