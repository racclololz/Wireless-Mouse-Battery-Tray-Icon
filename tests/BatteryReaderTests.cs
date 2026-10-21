using OP1wBatteryTray;

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

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

static Native.HIDP_VALUE_CAPS Cap(int min, int max, ushort bits) =>
    new() { LogicalMin = min, LogicalMax = max, BitSize = bits };

Check(Op1w.ParseResponse(Reply(0x08, 80), "test") is null, "cached 80% is rejected when mouse is unreachable");
Check(Op1w.ParseResponse(Reply(0x03, 80), "test") is null, "busy reply is rejected");
Check(Op1w.ParseResponse(Reply(0x01, 101), "test") is null, "invalid percentage is rejected");
Check(Op1w.ParseResponse(new byte[4], "test") is null, "short reply is rejected");
var reading = Op1w.ParseResponse(Reply(0x01, 45), "test");
Check(reading?.Percent == 45 && reading.VoltageMillivolts == 3786, "fresh 45% with voltage is accepted");

Check(HidBattery.Scale(87, Cap(0, 100, 8)) == 87, "0-100 battery strength is a percentage");
Check(HidBattery.Scale(128, Cap(0, -1, 8)) == 50, "unsigned 8-bit max encoded as -1 scales to 50%");
Check(HidBattery.Scale(255, Cap(0, 255, 8)) == 100, "0-255 full scale is 100%");
Check(HidBattery.Scale(101, Cap(0, 100, 8)) is null, "value above logical max is rejected");

namespace OP1wBatteryTray
{
    internal static class AppLog
    {
        internal static void Write(string message) { }
    }
}
