using static OP1wBatteryTray.Native;

namespace OP1wBatteryTray;

// Endgame Gear OP1w 4K v2 vendor protocol: SET feature A1 B4, then GET feature A1.
// response[1]: 01 = fresh answer from the mouse, 03 = dock busy, 08 = mouse not answering (dock repeats its cached values).
internal static class Op1w
{
    public const string VendorKey = "VID_3367";
    private const ushort DongleProductId = 0x1970;
    private const ushort WiredProductId = 0x1984;
    private const int AnswerTimeoutMs = 2000;
    private const int AnswerPollMs = 100;

    public static bool IsPresent() => EnumerateTargets().Any();

    public static BatteryReading? TryRead()
    {
        foreach (var target in EnumerateTargets())
        {
            var response = Query(target.Path);
            if (response is null) continue;
            AppLog.Write($"battery response: status=0x{response[1]:X2}, rawPercent={response[16]}");
            var reading = ParseResponse(response, target.Source);
            if (reading is not null) return reading;
        }

        return null;
    }

    internal static BatteryReading? ParseResponse(byte[] response, string source)
    {
        if (response.Length < 19 || response[0] != 0xA1 || response[1] != 0x01) return null;

        var percent = response[16];
        if (percent > 100) return null;

        int? voltage = null;
        var rawVoltage = response[17] | (response[18] << 8);
        if (rawVoltage is > 2500 and < 5000) voltage = rawVoltage;

        return new BatteryReading(percent, voltage, source);
    }

    public static IEnumerable<string> Diagnose()
    {
        var any = false;
        foreach (var target in EnumerateTargets())
        {
            any = true;
            yield return $"target: {target.Source}";
            yield return $"path: {target.Path}";
            var response = Query(target.Path);
            if (response is null)
            {
                yield return $"query failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}";
                continue;
            }

            yield return $"response[0..20]: {string.Join(" ", response.Take(21).Select(b => b.ToString("X2")))}";
            yield return $"marker: {response[1]:X2}{(response[1] == 0x01 ? "" : " (not a fresh answer)")}";
            yield return $"percent byte: {response[16]}";
        }

        if (!any) yield return "no OP1w battery feature interfaces found";
    }

    // Returns the last GET response (fresh or not), or null when the device cannot be opened or queried.
    private static byte[]? Query(string path)
    {
        using var handle = OpenHid(path, GENERIC_READ | GENERIC_WRITE);
        if (handle.IsInvalid) return null;

        var command = new byte[64];
        command[0] = 0xA1;
        command[1] = 0xB4;
        if (!HidD_SetFeature(handle, command, command.Length))
        {
            AppLog.Write($"battery request failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            return null;
        }

        var response = new byte[64];
        for (var waited = 0; waited < AnswerTimeoutMs; waited += AnswerPollMs)
        {
            Thread.Sleep(AnswerPollMs);
            Array.Clear(response);
            response[0] = 0xA1;
            if (!HidD_GetFeature(handle, response, response.Length))
            {
                AppLog.Write($"battery response failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                return null;
            }
            if (response[0] != 0xA1) return null;
            if (response[1] == 0x01) break;
        }

        return response;
    }

    private static IEnumerable<(string Path, string Source)> EnumerateTargets()
    {
        var candidates = new List<(string Path, bool Dongle)>();
        foreach (var path in Devices.Interfaces(Devices.HidInterface))
        {
            var lower = path.ToLowerInvariant();
            var dongle = lower.Contains("vid_3367&pid_1970");
            if (!dongle && !lower.Contains("vid_3367&pid_1984")) continue;
            if (!IsBatteryFeatureInterface(path)) continue;
            candidates.Add((path, dongle));
        }

        return candidates
            .OrderBy(c => c.Dongle ? 0 : 1)
            .Select(c => (c.Path, c.Dongle ? "wireless dock" : "wired mouse"));
    }

    private static bool IsBatteryFeatureInterface(string path)
    {
        using var handle = OpenHid(path, 0);
        if (handle.IsInvalid) return false;
        if (!HidD_GetPreparsedData(handle, out var preparsedData)) return false;

        try
        {
            return HidP_GetCaps(preparsedData, out var caps) == HIDP_STATUS_SUCCESS
                && caps.UsagePage == 0xFF01
                && caps.Usage == 0x0002
                && caps.FeatureReportByteLength >= 64;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }
}
