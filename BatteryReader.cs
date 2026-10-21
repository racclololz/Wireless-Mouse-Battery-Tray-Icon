using System.Collections.Concurrent;

namespace WirelessMouseBatteryTray;

internal enum PowerState { OnBattery, Charging, PluggedFull }

internal sealed record BatteryReading(
    int Percent,
    int? VoltageMillivolts,
    string Source,
    PowerState Power = PowerState.OnBattery,
    string? DeviceName = null);

internal interface IBatteryProvider
{
    string Name { get; }
    BatteryReading? TryRead(MouseTarget target);
    IEnumerable<string> Diagnose(MouseTarget target);
}

// One present mouse, resolved from its device key. Container groups every devnode of the same physical device or dongle.
internal sealed record MouseTarget(string DeviceKey, string InstanceId, Guid? Container, ushort Vid, ushort Pid)
{
    public bool HasOwnContainer => Container is Guid c && c != Devices.LocalMachineContainer;

    public IEnumerable<string> HidInterfaces() =>
        HasOwnContainer
            ? Devices.Interfaces(Devices.HidInterface).Where(p => Devices.InstanceId(p) is string id && Devices.ContainerId(id) == Container)
            : [];

    public static MouseTarget Absent(string deviceKey)
    {
        var ids = Devices.ParseIds(deviceKey);
        return new MouseTarget(deviceKey, "", null, ids?.Vid ?? 0, ids?.Pid ?? 0);
    }

    public static MouseTarget FromInstanceId(string instanceId)
    {
        var ids = Devices.ParseIds(instanceId);
        return new MouseTarget(Devices.DeviceKey(instanceId), instanceId, Devices.ContainerId(instanceId), ids?.Vid ?? 0, ids?.Pid ?? 0);
    }
}

internal sealed class BluetoothProvider : IBatteryProvider
{
    public string Name => "bluetooth";

    public BatteryReading? TryRead(MouseTarget target)
    {
        if (!target.HasOwnContainer) return null;
        foreach (var id in Devices.PresentInstanceIds())
        {
            if (Devices.ContainerId(id) == target.Container && Devices.BluetoothBattery(id) is int percent)
                return new BatteryReading(percent, null, "bluetooth");
        }

        return null;
    }

    public IEnumerable<string> Diagnose(MouseTarget target) =>
        target.HasOwnContainer
            ? Devices.PresentInstanceIds()
                .Where(id => Devices.ContainerId(id) == target.Container)
                .Select(id => $"{id}: {Devices.BluetoothBattery(id)?.ToString() ?? "-"}")
            : ["no own container"];
}

internal static class BatteryReader
{
    // Device-specific protocols first; the generic Windows sources last.
    public static readonly IBatteryProvider[] Providers =
    [
        new VendorFeatureProvider(),
        new LogitechProvider(),
        new BluetoothProvider(),
        new StandardHidProvider()
    ];

    private static readonly ConcurrentDictionary<string, IBatteryProvider> WorkingProvider = new(StringComparer.OrdinalIgnoreCase);

    // With no saved mouse, the first present mouse whose battery can be read is used.
    public static BatteryReading? TryRead(MouseChoice? mouse)
    {
        // A saved mouse that is not present still gets a target: the same model on another connection (cable vs dock) can answer.
        var targets = mouse is null ? PresentMice() : [FindMouse(mouse.DeviceKey) ?? MouseTarget.Absent(mouse.DeviceKey)];
        foreach (var target in targets)
        {
            var reading = TryRead(target);
            if (reading is not null) return reading;
        }

        return null;
    }

    public static BatteryReading? TryRead(MouseTarget target)
    {
        if (WorkingProvider.TryGetValue(target.DeviceKey, out var known) && Safe(known, target) is { } fast) return fast;

        foreach (var provider in Providers.Where(p => p != known))
        {
            if (Safe(provider, target) is not { } reading) continue;
            WorkingProvider[target.DeviceKey] = provider;
            return reading;
        }

        return null;
    }

    public static IEnumerable<string> Diagnose(MouseTarget target)
    {
        yield return $"mouse: {target.InstanceId}";
        yield return $"ids: VID {target.Vid:X4} PID {target.Pid:X4}, container {target.Container}";
        foreach (var provider in Providers)
        {
            yield return $"  [{provider.Name}]";
            List<string> lines;
            try
            {
                lines = provider.Diagnose(target).ToList();
            }
            catch (Exception error)
            {
                lines = [$"error: {error.Message}"];
            }

            foreach (var line in lines) yield return "    " + line;
        }
    }

    public static IReadOnlyList<MouseTarget> PresentMice() =>
        Devices.Interfaces(Devices.MouseInterface)
            .Select(Devices.InstanceId)
            .OfType<string>()
            .Select(MouseTarget.FromInstanceId)
            .DistinctBy(t => t.DeviceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static MouseTarget? FindMouse(string deviceKey) =>
        PresentMice().FirstOrDefault(t => t.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));

    private static BatteryReading? Safe(IBatteryProvider provider, MouseTarget target)
    {
        try
        {
            return provider.TryRead(target);
        }
        catch (Exception error)
        {
            AppLog.Write($"{provider.Name} failed: {error.Message}");
            return null;
        }
    }
}
