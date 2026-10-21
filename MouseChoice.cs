using System.Text.Json;
using System.Text.Json.Serialization;

namespace OP1wBatteryTray;

internal sealed record MouseChoice(string DeviceKey, string Name)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OP1wBatteryTray", "mouse.json");

    [JsonIgnore]
    public bool IsOp1w => DeviceKey.Contains(Op1w.VendorKey, StringComparison.OrdinalIgnoreCase);

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
