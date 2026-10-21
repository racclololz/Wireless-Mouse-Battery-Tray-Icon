using WirelessMouseBatteryTray;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine($"PASS: {name}");
}

static Native.HIDP_VALUE_CAPS Cap(int min, int max, ushort bits) =>
    new() { LogicalMin = min, LogicalMax = max, BitSize = bits };

// OP1w via the device list
var op1w = DeviceList.VendorFeature.Single(d => d.Name == "OP1w 4K v2");
static byte[] Reply(byte status, byte percent)
{
    var result = new byte[64];
    result[0] = 0xA1;
    result[1] = status;
    result[16] = percent;
    result[17] = 0xCA;
    result[18] = 0x0E;
    return result;
}

Check(VendorFeatureProvider.ParseResponse(op1w, Reply(0x08, 80), false) is null, "OP1w cached 80% is rejected when mouse is unreachable");
Check(VendorFeatureProvider.ParseResponse(op1w, Reply(0x03, 80), false) is null, "OP1w busy reply is rejected");
Check(VendorFeatureProvider.ParseResponse(op1w, Reply(0x01, 101), false) is null, "OP1w invalid percentage is rejected");
Check(VendorFeatureProvider.ParseResponse(op1w, new byte[4], false) is null, "OP1w short reply is rejected");
var reading = VendorFeatureProvider.ParseResponse(op1w, Reply(0x01, 45), false);
Check(reading is { Percent: 45, VoltageMillivolts: 3786, Power: PowerState.OnBattery }, "OP1w fresh 45% wireless is on battery");
Check(VendorFeatureProvider.ParseResponse(op1w, Reply(0x01, 45), true)?.Power == PowerState.Charging, "OP1w on cable below 100% is charging");
Check(VendorFeatureProvider.ParseResponse(op1w, Reply(0x01, 100), true)?.Power == PowerState.PluggedFull, "OP1w on cable at 100% is plugged in");
Check(VendorFeatureProvider.Match(new MouseTarget("k", "", null, 0x3367, 0x1984)) == op1w, "OP1w wired PID matches the same entry");

// Device ids
Check(Devices.ParseIds(@"HID\VID_046D&PID_C539&MI_01&Col01") == (0x046D, 0xC539), "USB ids parse");
Check(Devices.ParseIds(@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b023&Col01") == (0x046D, 0xB023), "Bluetooth classic ids parse");
Check(Devices.ParseIds(@"HID\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02046d_PID&b023_REV&0011") == (0x046D, 0xB023), "Bluetooth LE ids parse");

// Standard HID scaling
Check(StandardHidProvider.Scale(87, Cap(0, 100, 8)) == 87, "0-100 battery strength is a percentage");
Check(StandardHidProvider.Scale(128, Cap(0, -1, 8)) == 50, "unsigned 8-bit max encoded as -1 scales to 50%");
Check(StandardHidProvider.Scale(101, Cap(0, 100, 8)) is null, "value above logical max is rejected");

// Logitech HID++ against a simulated receiver: index 1 keyboard, index 2 mouse
var receiver = new FakeReceiver();
receiver.Devices[1] = new FakeHidpp(type: 0, name: "K380", features: new() { [0x0005] = 1, [0x1004] = 2 }, unified: [90, 8, 0, 0]);
receiver.Devices[2] = new FakeHidpp(type: 3, name: "G Pro X Superlight", features: new() { [0x0005] = 1, [0x1004] = 2 }, unified: [76, 4, 0, 0]);
var mouse = new HidppDevice(receiver, 2, []);
Check(mouse.Ping(), "HID++ ping answers on a paired index");
Check(!new HidppDevice(receiver, 4, []).Ping(), "HID++ ping times out on an empty index");
Check(mouse.DeviceType() == 3, "HID++ device type reads mouse");
Check(mouse.Name() == "G Pro X Superlight", "HID++ name reads across chunks");
Check(mouse.ReadBattery() == new HidppBattery(76, PowerState.OnBattery, null), "HID++ unified battery reads 76% discharging");

receiver.Devices[3] = new FakeHidpp(type: 3, name: "M720", features: new() { [0x1000] = 3 }, status: [40, 30, 1]);
Check(new HidppDevice(receiver, 3, []).ReadBattery() == new HidppBattery(40, PowerState.Charging, null), "HID++ battery status falls back and reads charging");

receiver.Devices[5] = new FakeHidpp(type: 3, name: "G Pro", features: new() { [0x1001] = 4 }, voltage: [0x0F, 0x16, 0x00]);
Check(new HidppDevice(receiver, 5, []).ReadBattery() == new HidppBattery(HidppDevice.VoltageToPercent(3862), PowerState.OnBattery, 3862), "HID++ voltage feature maps 3862 mV");
Check(HidppDevice.VoltageToPercent(3859) == 60 && HidppDevice.VoltageToPercent(4300) == 100 && HidppDevice.VoltageToPercent(3400) == 0, "voltage curve end points");

// Tracker: steady number, time left, low battery signal
var tracker = new BatteryTracker();
var t0 = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);
foreach (var (minute, value) in new[] { (0, 30), (1, 25), (2, 30), (3, 25), (4, 30) })
    tracker.Add(new BatteryReading(value, null, "test"), t0.AddMinutes(minute));
Check(tracker.Percent == 30, "median holds 30 while the reading steps between 25 and 30");

tracker = new BatteryTracker();
for (var hour = 0; hour <= 4; hour++)
    for (var i = 0; i < 5; i++)
        tracker.Add(new BatteryReading(80 - hour * 5, null, "test"), t0.AddHours(hour).AddMinutes(i));
var left = tracker.TimeLeft(t0.AddHours(4).AddMinutes(4));
Check(left is { TotalHours: > 11 and < 13 }, $"time left from 80% to 60% in 4 h is about 12 h (got {left?.TotalHours:F1})");
var fresh = new BatteryTracker();
fresh.Add(new BatteryReading(80, null, "test"), t0);
fresh.Add(new BatteryReading(75, null, "test"), t0.AddHours(1));
Check(fresh.TimeLeft(t0.AddHours(1)) is null, "time left waits for a 10-point drop");

tracker = new BatteryTracker();
for (var i = 0; i < 5; i++) tracker.Add(new BatteryReading(15, null, "test"), t0.AddMinutes(i));
Check(tracker.TakeLowBatterySignal(), "low battery signals at 15%");
Check(!tracker.TakeLowBatterySignal(), "low battery signals only once");
tracker.Add(new BatteryReading(16, null, "test", PowerState.Charging), t0.AddMinutes(10));
Check(!tracker.TakeLowBatterySignal(), "charging rearms without a signal");
for (var i = 0; i < 5; i++) tracker.Add(new BatteryReading(12, null, "test"), t0.AddMinutes(20 + i));
Check(tracker.TakeLowBatterySignal(), "low battery signals again after charging");

internal sealed class FakeHidpp(int type, string name, Dictionary<ushort, byte> features, byte[]? unified = null, byte[]? status = null, byte[]? voltage = null)
{
    public byte[]? Answer(byte featureIndex, byte function, byte[] args)
    {
        if (featureIndex == 0 && function == 0)
        {
            var feature = (ushort)((args[0] << 8) | args[1]);
            return [features.TryGetValue(feature, out var index) ? index : (byte)0, 0, 0];
        }

        if (featureIndex == 0 && function == 1) return [4, 2, args[2]];
        var id = features.FirstOrDefault(f => f.Value == featureIndex).Key;
        return (id, function) switch
        {
            (0x0005, 0) => [(byte)name.Length],
            (0x0005, 1) => System.Text.Encoding.UTF8.GetBytes(name.PadRight(64, '\0')).Skip(args[0]).Take(16).ToArray(),
            (0x0005, 2) => [(byte)type],
            (0x1004, 1) => unified,
            (0x1000, 0) => status,
            (0x1001, 0) => voltage,
            _ => null
        };
    }
}

internal sealed class FakeReceiver : IHidppChannel
{
    public Dictionary<byte, FakeHidpp> Devices { get; } = [];

    public byte[]? Exchange(byte[] request, int timeoutMs)
    {
        if (!Devices.TryGetValue(request[1], out var device)) return null;
        var answer = device.Answer(request[2], (byte)(request[3] >> 4), request[4..]);
        var reply = new byte[20];
        request[..4].CopyTo(reply, 0);
        if (answer is null)
        {
            reply[2] = 0xFF;
            reply[3] = request[2];
            reply[4] = request[3];
            reply[5] = 0x02;
        }
        else
        {
            answer.Take(16).ToArray().CopyTo(reply, 4);
        }

        return HidppDevice.IsReplyTo(request, reply) ? reply : null;
    }
}

namespace WirelessMouseBatteryTray
{
    internal static class AppLog
    {
        internal static void Write(string message) { }
    }
}
