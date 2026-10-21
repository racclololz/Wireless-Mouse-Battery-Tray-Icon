using System.Text.Json;

namespace WirelessMouseBatteryTray;

internal sealed record MouseChoice(string DeviceKey, string Name)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WirelessMouseBatteryTray", "mouse.json");

    public static bool Exists => File.Exists(FilePath);

    public static MouseChoice? Load()
    {
        try
        {
            var choice = JsonSerializer.Deserialize<MouseChoice>(File.ReadAllText(FilePath));
            return string.IsNullOrWhiteSpace(choice?.DeviceKey) ? null : choice;
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
