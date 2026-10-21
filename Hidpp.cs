namespace WirelessMouseBatteryTray;

// Logitech HID++ 2.0 over long reports (20 bytes): [0x11, deviceIndex, featureIndex, function<<4 | softwareId, params...].
// Protocol reference: Solaar / libratbag (public). Device index 0xFF = directly connected (USB cable, Bluetooth), 1..6 = paired to a receiver.
internal interface IHidppChannel
{
    // Sends one long report and returns the reply that Hidpp.IsReplyTo accepts, or null on timeout.
    byte[]? Exchange(byte[] request, int timeoutMs);
}

internal sealed record HidppBattery(int Percent, PowerState Power, int? VoltageMillivolts);

internal sealed class HidppDevice(IHidppChannel channel, byte index, Dictionary<ushort, byte?> featureCache)
{
    public const byte LongReport = 0x11;
    public const byte DirectIndex = 0xFF;
    private const byte SoftwareId = 0x0B;
    private const int TimeoutMs = 700;

    private const ushort RootFeature = 0x0000;
    private const ushort DeviceNameType = 0x0005;
    private const ushort BatteryStatus = 0x1000;
    private const ushort BatteryVoltage = 0x1001;
    private const ushort UnifiedBattery = 0x1004;

    public byte Index => index;

    public static bool IsReplyTo(byte[] request, byte[] reply)
    {
        if (reply.Length < 7 || reply[1] != request[1]) return false;
        if (reply[0] == LongReport && reply[2] == request[2] && reply[3] == request[3]) return true;
        // HID++ 2.0 error (0xFF) and HID++ 1.0 error (0x8F) echo the request's feature index and function.
        return reply[2] is 0xFF or 0x8F && reply[3] == request[2] && reply[4] == request[3];
    }

    // Returns the reply parameters (bytes 4..), or null on timeout or error.
    public byte[]? Call(byte featureIndex, byte function, params byte[] args)
    {
        var request = new byte[20];
        request[0] = LongReport;
        request[1] = index;
        request[2] = featureIndex;
        request[3] = (byte)((function << 4) | SoftwareId);
        args.CopyTo(request, 4);
        var reply = channel.Exchange(request, TimeoutMs);
        if (reply is null || reply[2] is 0xFF or 0x8F) return null;
        return reply[4..];
    }

    public bool Ping() => Call(0x00, 1, 0, 0, 0x5A) is { } reply && reply[2] == 0x5A;

    public byte? FeatureIndex(ushort feature)
    {
        if (featureCache.TryGetValue(feature, out var cached)) return cached;
        var reply = Call(0x00, 0, (byte)(feature >> 8), (byte)feature);
        if (reply is null) return null;
        byte? found = reply[0] == 0 ? null : reply[0];
        featureCache[feature] = found;
        return found;
    }

    // 0 keyboard, 1 remote, 2 numpad, 3 mouse, 4 trackpad, 5 trackball, ...
    public int? DeviceType() =>
        FeatureIndex(DeviceNameType) is byte f && Call(f, 2) is { } reply ? reply[0] : null;

    public string? Name()
    {
        if (FeatureIndex(DeviceNameType) is not byte f || Call(f, 0) is not { } count) return null;
        var length = count[0];
        var chars = new List<byte>();
        while (chars.Count < length)
        {
            if (Call(f, 1, (byte)chars.Count) is not { } chunk) break;
            var take = Math.Min(16, length - chars.Count);
            chars.AddRange(chunk.Take(take));
            if (take == 0) break;
        }

        var name = System.Text.Encoding.UTF8.GetString(chars.ToArray()).TrimEnd('\0').Trim();
        return name.Length > 0 ? name : null;
    }

    public HidppBattery? ReadBattery()
    {
        if (FeatureIndex(UnifiedBattery) is byte unified && Call(unified, 1) is { } u)
        {
            // [stateOfCharge%, levelFlags, chargingStatus, externalPower]; some devices report only level flags.
            var percent = u[0] != 0 ? u[0] : (u[1] & 8) != 0 ? 100 : (u[1] & 4) != 0 ? 50 : (u[1] & 2) != 0 ? 20 : (u[1] & 1) != 0 ? 5 : 0;
            var power = u[2] switch { 1 or 2 => PowerState.Charging, 3 => PowerState.PluggedFull, _ => PowerState.OnBattery };
            if (percent is > 0 and <= 100) return new HidppBattery(percent, power, null);
        }

        if (FeatureIndex(BatteryStatus) is byte status && Call(status, 0) is { } s)
        {
            // [level%, nextLevel%, status]: 0 discharging, 1 recharging, 2 almost full, 3 charged, 4 slow recharge.
            var power = s[2] switch { 1 or 2 or 4 => PowerState.Charging, 3 => PowerState.PluggedFull, _ => PowerState.OnBattery };
            var percent = power == PowerState.PluggedFull ? 100 : s[0];
            if (percent is > 0 and <= 100) return new HidppBattery(percent, power, null);
        }

        if (FeatureIndex(BatteryVoltage) is byte voltage && Call(voltage, 0) is { } v)
        {
            // [voltage mV big-endian, flags]: flags bit 7 = charging, bits 0..2 = charge status (3 = full).
            var millivolts = (v[0] << 8) | v[1];
            var charging = (v[2] & 0x80) != 0;
            var power = !charging ? PowerState.OnBattery : (v[2] & 0x07) == 3 ? PowerState.PluggedFull : PowerState.Charging;
            if (millivolts is > 2500 and < 5000) return new HidppBattery(VoltageToPercent(millivolts), power, millivolts);
        }

        return null;
    }

    // Li-ion discharge curve used by Solaar for 0x1001 devices.
    private static readonly (int Percent, int Millivolts)[] Curve =
    [
        (100, 4186), (90, 4067), (80, 3989), (70, 3922), (60, 3859), (50, 3811),
        (40, 3778), (30, 3751), (20, 3717), (10, 3671), (5, 3646), (2, 3579), (0, 3500)
    ];

    internal static int VoltageToPercent(int millivolts)
    {
        if (millivolts >= Curve[0].Millivolts) return 100;
        for (var i = 1; i < Curve.Length; i++)
        {
            var (highP, highV) = Curve[i - 1];
            var (lowP, lowV) = Curve[i];
            if (millivolts >= lowV)
                return (int)Math.Round(lowP + (highP - lowP) * (millivolts - lowV) / (double)(highV - lowV));
        }

        return 0;
    }
}
