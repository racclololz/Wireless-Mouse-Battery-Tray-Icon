namespace WirelessMouseBatteryTray;

// Turns raw readings into what the tray shows: a steady percentage, a time-left estimate, and a one-shot low-battery signal.
internal sealed class BatteryTracker
{
    private const int Window = 5;
    private const int MinDropForEstimate = 10;
    public const int LowThreshold = 15;
    private const int RearmThreshold = 20;

    private readonly List<int> _recent = [];
    private PowerState? _power;
    private (DateTime Time, int Percent)? _dischargeStart;
    private bool _lowSignalled;

    public int? Percent { get; private set; }
    public PowerState Power => _power ?? PowerState.OnBattery;

    public void Reset()
    {
        _recent.Clear();
        _power = null;
        _dischargeStart = null;
        _lowSignalled = false;
        Percent = null;
    }

    // Median of the last readings: some mice step between two neighbouring values (25/30) from one minute to the next.
    public int Add(BatteryReading reading, DateTime now)
    {
        if (_power != reading.Power)
        {
            _recent.Clear();
            _dischargeStart = null;
            _power = reading.Power;
        }

        _recent.Add(reading.Percent);
        if (_recent.Count > Window) _recent.RemoveAt(0);
        var sorted = _recent.Order().ToList();
        var percent = sorted[sorted.Count / 2];
        Percent = percent;

        if (reading.Power == PowerState.OnBattery)
        {
            // A higher value while on battery means the mouse was charged elsewhere; start the estimate again.
            if (_dischargeStart is { } start && percent > start.Percent) _dischargeStart = null;
            _dischargeStart ??= (now, percent);
        }

        return percent;
    }

    public TimeSpan? TimeLeft(DateTime now)
    {
        if (Power != PowerState.OnBattery || _dischargeStart is not { } start || Percent is not int percent) return null;
        var drop = start.Percent - percent;
        if (drop < MinDropForEstimate) return null;
        var hours = (now - start.Time).TotalHours;
        if (hours <= 0) return null;
        return TimeSpan.FromHours(percent / (drop / hours));
    }

    // True once when the battery reaches the low threshold; true again only after charging or a rise above the rearm level.
    public bool TakeLowBatterySignal()
    {
        if (Percent is not int percent) return false;
        if (Power != PowerState.OnBattery || percent > RearmThreshold)
        {
            _lowSignalled = false;
            return false;
        }

        if (percent > LowThreshold || _lowSignalled) return false;
        _lowSignalled = true;
        return true;
    }

    public static string Format(TimeSpan time) =>
        time.TotalHours >= 48 ? $"about {(int)Math.Round(time.TotalDays)} days left"
        : time.TotalHours >= 1 ? $"about {(int)Math.Round(time.TotalHours)} h left"
        : $"about {Math.Max(1, (int)Math.Round(time.TotalMinutes))} min left";
}
