using System.Runtime.InteropServices;
using static WirelessMouseBatteryTray.Native;

namespace WirelessMouseBatteryTray;

// Devices listed in devices.json under "vendorFeature".
// For the OP1w 4K v2 dock: status 01 = fresh answer, 03 = busy, 08 = mouse not answering (the dock repeats cached values).
internal sealed class VendorFeatureProvider : IBatteryProvider
{
    private const int AnswerTimeoutMs = 2000;
    private const int AnswerPollMs = 100;

    public string Name => "vendor feature";

    public BatteryReading? TryRead(MouseTarget target)
    {
        var device = Match(target);
        if (device is null) return null;

        foreach (var (path, pid) in Interfaces(device))
        {
            var response = Query(device, path);
            if (response is null) continue;
            AppLog.Write($"{device.Name} response: status=0x{response[device.StatusOffset]:X2}, rawPercent={response[device.PercentOffset]}");
            var reading = ParseResponse(device, response, device.IsWired(pid));
            if (reading is not null) return reading;
        }

        return null;
    }

    public IEnumerable<string> Diagnose(MouseTarget target)
    {
        var device = Match(target);
        if (device is null)
        {
            yield return "not in device list";
            yield break;
        }

        foreach (var (path, pid) in Interfaces(device))
        {
            var response = Query(device, path);
            yield return $"{path} ({(device.IsWired(pid) ? "wired" : "wireless")}): " + (response is null
                ? $"query failed {Marshal.GetLastWin32Error()}"
                : string.Join(" ", response.Take(24).Select(b => b.ToString("X2"))));
        }
    }

    internal static VendorFeatureDevice? Match(MouseTarget target) =>
        DeviceList.VendorFeature.FirstOrDefault(d => d.VendorId == target.Vid && d.HasProduct(target.Pid));

    internal static BatteryReading? ParseResponse(VendorFeatureDevice device, byte[] response, bool wired)
    {
        var minLength = Math.Max(device.PercentOffset, (device.VoltageOffset ?? 0) + 1) + 1;
        if (response.Length < minLength || response[0] != device.ReportIdValue) return null;
        if (response[device.StatusOffset] != device.FreshStatusValue) return null;

        var percent = response[device.PercentOffset];
        if (percent > 100) return null;

        int? voltage = null;
        if (device.VoltageOffset is int offset)
        {
            var raw = response[offset] | (response[offset + 1] << 8);
            if (raw is > 2500 and < 5000) voltage = raw;
        }

        // On its cable the mouse charges.
        var power = !wired ? PowerState.OnBattery : percent < 100 ? PowerState.Charging : PowerState.PluggedFull;
        return new BatteryReading(percent, voltage, wired ? "wired" : "wireless", power, device.Name);
    }

    private static byte[]? Query(VendorFeatureDevice device, string path)
    {
        using var handle = OpenHid(path, GENERIC_READ | GENERIC_WRITE);
        if (handle.IsInvalid) return null;

        var command = new byte[device.ReportLength];
        device.CommandBytes.CopyTo(command, 0);
        if (!HidD_SetFeature(handle, command, command.Length))
        {
            AppLog.Write($"{device.Name} request failed: {Marshal.GetLastWin32Error()}");
            return null;
        }

        var response = new byte[device.ReportLength];
        for (var waited = 0; waited < AnswerTimeoutMs; waited += AnswerPollMs)
        {
            Thread.Sleep(AnswerPollMs);
            Array.Clear(response);
            response[0] = device.ReportIdValue;
            if (!HidD_GetFeature(handle, response, response.Length))
            {
                AppLog.Write($"{device.Name} response failed: {Marshal.GetLastWin32Error()}");
                return null;
            }

            if (response[device.StatusOffset] == device.FreshStatusValue) break;
        }

        return response;
    }

    // Wired interfaces first: when the cable is in, that is where the mouse answers.
    private static IEnumerable<(string Path, ushort Pid)> Interfaces(VendorFeatureDevice device) =>
        Devices.Interfaces(Devices.HidInterface)
            .Select(path => (Path: path, Ids: Devices.ParseIds(path)))
            .Where(x => x.Ids is { } ids && ids.Vid == device.VendorId && device.HasProduct(ids.Pid))
            .Where(x => Devices.HidCaps(x.Path) is { } caps
                && caps.UsagePage == device.UsagePageValue
                && caps.Usage == device.UsageValue
                && caps.FeatureReportByteLength >= device.ReportLength)
            .Select(x => (x.Path, x.Ids!.Value.Pid))
            .OrderBy(x => device.IsWired(x.Pid) ? 0 : 1)
            .ToList();
}
