using System.Globalization;
using System.Text.Json;

namespace WirelessMouseBatteryTray;

// Mice read with a vendor feature report: SET <command>, then GET until byte[statusOffset] == freshStatus.
// Adding a mouse that uses this pattern is a devices.json entry, not code.
internal sealed record VendorFeatureDevice(
    string Name,
    string Vid,
    Dictionary<string, string> Pids,
    string UsagePage,
    string Usage,
    int ReportLength,
    string Command,
    string ReportId,
    int StatusOffset,
    string FreshStatus,
    int PercentOffset,
    int? VoltageOffset,
    string? ConfigTool)
{
    public ushort VendorId => Hex16(Vid);
    public ushort UsagePageValue => Hex16(UsagePage);
    public ushort UsageValue => Hex16(Usage);
    public byte ReportIdValue => Convert.ToByte(ReportId, 16);
    public byte FreshStatusValue => Convert.ToByte(FreshStatus, 16);
    public byte[] CommandBytes => Convert.FromHexString(Command);
    public bool HasProduct(ushort pid) => Pids.Keys.Any(p => Hex16(p) == pid);
    public bool IsWired(ushort pid) => Pids.Any(p => Hex16(p.Key) == pid && p.Value == "wired");

    private static ushort Hex16(string value) => ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

internal sealed record DeviceListData(List<VendorFeatureDevice> VendorFeature);

internal static class DeviceList
{
    private static readonly Lazy<DeviceListData> Data = new(Load);

    public static IReadOnlyList<VendorFeatureDevice> VendorFeature => Data.Value.VendorFeature;

    public static DeviceListData Parse(string json) =>
        JsonSerializer.Deserialize<DeviceListData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? new DeviceListData([]);

    private static DeviceListData Load()
    {
        using var stream = typeof(DeviceList).Assembly.GetManifestResourceStream("WirelessMouseBatteryTray.devices.json");
        if (stream is null) return new DeviceListData([]);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
