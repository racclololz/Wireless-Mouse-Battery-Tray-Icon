using static WirelessMouseBatteryTray.Native;

namespace WirelessMouseBatteryTray;

// Standard HID battery usages: Generic Device Controls / Battery Strength, Battery System / Absolute State Of Charge or Remaining Capacity.
internal sealed class StandardHidProvider : IBatteryProvider
{
    private const int InputReport = 0;
    private const int FeatureReport = 2;

    public string Name => "standard HID";

    public BatteryReading? TryRead(MouseTarget target)
    {
        foreach (var path in target.HidInterfaces())
        {
            if (ReadPath(path) is int percent) return new BatteryReading(percent, null, "HID");
        }

        return null;
    }

    public IEnumerable<string> Diagnose(MouseTarget target) =>
        target.HidInterfaces().Select(path => $"{path}: {ReadPath(path)?.ToString() ?? "-"}");

    private static bool IsBatteryUsage(in HIDP_VALUE_CAPS cap) =>
        cap.IsRange == 0 &&
        ((cap.UsagePage == 0x06 && cap.Usage == 0x20) ||
         (cap.UsagePage == 0x85 && cap.Usage is 0x65 or 0x66));

    internal static int? ReadPath(string path)
    {
        using var handle = OpenHid(path, GENERIC_READ | GENERIC_WRITE);
        using var fallback = handle.IsInvalid ? OpenHid(path, 0) : null;
        var h = fallback ?? handle;
        if (h.IsInvalid || !HidD_GetPreparsedData(h, out var preparsed)) return null;

        try
        {
            if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS) return null;

            foreach (var (type, count, length) in new[]
                     {
                         (FeatureReport, caps.NumberFeatureValueCaps, caps.FeatureReportByteLength),
                         (InputReport, caps.NumberInputValueCaps, caps.InputReportByteLength)
                     })
            {
                if (count == 0 || length == 0) continue;
                var n = count;
                var values = new HIDP_VALUE_CAPS[n];
                if (HidP_GetValueCaps(type, values, ref n, preparsed) != HIDP_STATUS_SUCCESS) continue;

                foreach (var cap in values.Take(n).Where(c => IsBatteryUsage(c)))
                {
                    var report = new byte[length];
                    report[0] = cap.ReportID;
                    var ok = type == FeatureReport
                        ? HidD_GetFeature(h, report, report.Length)
                        : HidD_GetInputReport(h, report, report.Length);
                    if (!ok) continue;
                    if (HidP_GetUsageValue(type, cap.UsagePage, cap.LinkCollection, cap.Usage, out var raw, preparsed, report, (uint)report.Length) != HIDP_STATUS_SUCCESS)
                        continue;

                    var percent = Scale(raw, cap);
                    if (percent is not null) return percent;
                }
            }

            return null;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    internal static int? Scale(uint raw, in HIDP_VALUE_CAPS cap)
    {
        long min = cap.LogicalMin, max = cap.LogicalMax;
        // Descriptors often encode an unsigned max like 255 as signed (-1).
        if (max <= min && cap.BitSize is > 0 and < 32) { min = 0; max = (1L << cap.BitSize) - 1; }
        if (max <= min || raw < min || raw > max) return null;
        return (int)Math.Round((raw - min) * 100.0 / (max - min));
    }
}
