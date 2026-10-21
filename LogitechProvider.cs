using System.Collections.Concurrent;
using static WirelessMouseBatteryTray.Native;

namespace WirelessMouseBatteryTray;

// Logitech mice (VID 046D) on Unifying, Bolt or Lightspeed receivers, USB cable or Bluetooth.
internal sealed class LogitechProvider : IBatteryProvider
{
    private const ushort LogitechVid = 0x046D;
    private static readonly byte[] Indexes = [HidppDevice.DirectIndex, 1, 2, 3, 4, 5, 6];
    private static readonly ConcurrentDictionary<(string Path, byte Index), Dictionary<ushort, byte?>> Features = new();
    private static readonly ConcurrentDictionary<string, byte> KnownIndex = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "logitech";

    public BatteryReading? TryRead(MouseTarget target)
    {
        if (target.Vid != LogitechVid) return null;

        foreach (var path in LongReportInterfaces(target))
        {
            using var channel = HidppChannel.Open(path);
            if (channel is null) continue;

            var order = KnownIndex.TryGetValue(path, out var known) ? Indexes.Prepend(known).Distinct() : Indexes;
            foreach (var index in order)
            {
                var device = new HidppDevice(channel, index, Features.GetOrAdd((path, index), _ => []));
                if (!device.Ping()) continue;
                // A receiver can also hold a keyboard; skip devices that say they are not pointing devices.
                if (index != HidppDevice.DirectIndex && device.DeviceType() is int type && type is not (3 or 4 or 5)) continue;
                if (device.ReadBattery() is not { } battery) continue;

                KnownIndex[path] = index;
                return new BatteryReading(battery.Percent, battery.VoltageMillivolts, index == HidppDevice.DirectIndex ? "logitech" : "logitech receiver",
                    battery.Power, device.Name());
            }
        }

        return null;
    }

    public IEnumerable<string> Diagnose(MouseTarget target)
    {
        if (target.Vid != LogitechVid)
        {
            yield return "not a Logitech device";
            yield break;
        }

        var paths = LongReportInterfaces(target).ToList();
        if (paths.Count == 0) yield return "no HID++ long-report interface in this device";
        foreach (var path in paths)
        {
            using var channel = HidppChannel.Open(path);
            if (channel is null)
            {
                yield return $"{path}: open failed {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}";
                continue;
            }

            foreach (var index in Indexes)
            {
                var device = new HidppDevice(channel, index, []);
                if (!device.Ping()) continue;
                yield return $"{path} index {index:X2}: type {device.DeviceType()?.ToString() ?? "?"}, name {device.Name() ?? "?"}, battery {device.ReadBattery()?.ToString() ?? "-"}";
            }
        }
    }

    // HID++ long reports live on a vendor collection (usage page FF00 on receivers, FF43 on Bluetooth) with 20-byte reports.
    private static IEnumerable<string> LongReportInterfaces(MouseTarget target) =>
        target.HidInterfaces().Where(path => Devices.HidCaps(path) is { } caps
            && (caps.UsagePage & 0xFF00) == 0xFF00
            && caps.InputReportByteLength == 20
            && caps.OutputReportByteLength == 20);
}

internal sealed class HidppChannel : IHidppChannel, IDisposable
{
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private readonly FileStream _stream;

    private HidppChannel(FileStream stream) => _stream = stream;

    public static HidppChannel? Open(string path)
    {
        var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new HidppChannel(new FileStream(handle, FileAccess.ReadWrite, 0, true));
    }

    public byte[]? Exchange(byte[] request, int timeoutMs)
    {
        try
        {
            _stream.Write(request, 0, request.Length);
        }
        catch (IOException)
        {
            return null;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0) return null;

            var buffer = new byte[20];
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(remaining));
                var read = _stream.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
                if (read > 0 && HidppDevice.IsReplyTo(request, buffer)) return buffer;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
