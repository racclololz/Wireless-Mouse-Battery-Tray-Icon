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
    bool AllProducts = false,
    string? WakeCommand = null,
    PollingRateSetting? PollingRate = null)
{
    public ushort VendorId => Hex16(Vid);
    public ushort UsagePageValue => Hex16(UsagePage);
    public ushort UsageValue => Hex16(Usage);
    public byte ReportIdValue => Convert.ToByte(ReportId, 16);
    public byte FreshStatusValue => Convert.ToByte(FreshStatus, 16);
    public byte[] CommandBytes => Convert.FromHexString(Command);
    public byte[]? WakeBytes => WakeCommand is null ? null : Convert.FromHexString(WakeCommand);
    // AllProducts: any product of this vendor that exposes the matching collection; Pids then only mark wired connections.
    public bool HasProduct(ushort pid) => AllProducts || Pids.Keys.Any(p => Hex16(p) == pid);
    public bool IsWired(ushort pid) => Pids.Any(p => Hex16(p.Key) == pid && p.Value == "wired");
    public PollingRateSetting? PollingRateFor(ushort pid) => PollingRate is { } p && p.Pids.Any(x => Hex16(x) == pid) ? p : null;

    private static ushort Hex16(string value) => ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

// Read: SET readCommand, the rate code is at readOffset of the fresh answer. Write: writeCommand with {rate} replaced by the code.
// The OP1w 4K v2 write also carries the other settings of its group (bytes after {rate}); those are the values its own tool wrote.
internal sealed record PollingRateSetting(List<string> Pids, string ReadCommand, int ReadOffset, string WriteCommand, List<PollingRateOption> Rates)
{
    public byte[] ReadBytes => Convert.FromHexString(ReadCommand);
    public byte[] WriteBytes(byte rate) => Convert.FromHexString(WriteCommand.Replace("{rate}", rate.ToString("X2")));
}

internal sealed record PollingRateOption(string Label, string Value)
{
    public byte Code => Convert.ToByte(Value, 16);
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
