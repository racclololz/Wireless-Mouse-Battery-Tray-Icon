namespace OP1wBatteryTray;

internal sealed record BatteryReading(int Percent, int? VoltageMillivolts, string Source);

internal static class BatteryReader
{
    public static BatteryReading? TryRead(MouseChoice? mouse) =>
        mouse is null || mouse.IsOp1w ? Op1w.TryRead() : TryReadStandard(mouse.DeviceKey);

    public static IEnumerable<string> Diagnose(MouseChoice? mouse)
    {
        if (mouse is null || mouse.IsOp1w)
        {
            foreach (var line in Op1w.Diagnose()) yield return line;
            yield break;
        }

        yield return $"mouse: {mouse.Name} ({mouse.DeviceKey})";
        var instanceId = FindMouse(mouse.DeviceKey);
        if (instanceId is null)
        {
            yield return "mouse not connected";
            yield break;
        }

        var container = Devices.ContainerId(instanceId);
        yield return $"instance: {instanceId}";
        yield return $"container: {container}";
        if (container is null || container == Devices.LocalMachineContainer) yield break;

        foreach (var id in Devices.PresentInstanceIds().Where(id => Devices.ContainerId(id) == container))
            yield return $"devnode: {id} bluetooth battery: {Devices.BluetoothBattery(id)?.ToString() ?? "-"}";

        foreach (var path in Devices.Interfaces(Devices.HidInterface).Where(p => InContainer(p, container.Value)))
            yield return $"hid: {path} battery usage: {HidBattery.TryRead(path)?.ToString() ?? "-"}";
    }

    private static BatteryReading? TryReadStandard(string deviceKey)
    {
        var instanceId = FindMouse(deviceKey);
        if (instanceId is null) return null;

        var container = Devices.ContainerId(instanceId);
        if (container is null || container == Devices.LocalMachineContainer) return null;

        foreach (var id in Devices.PresentInstanceIds())
        {
            if (Devices.ContainerId(id) == container && Devices.BluetoothBattery(id) is int percent)
                return new BatteryReading(percent, null, "bluetooth");
        }

        foreach (var path in Devices.Interfaces(Devices.HidInterface))
        {
            if (InContainer(path, container.Value) && HidBattery.TryRead(path) is int percent)
                return new BatteryReading(percent, null, "hid");
        }

        return null;
    }

    private static string? FindMouse(string deviceKey) =>
        Devices.Interfaces(Devices.MouseInterface)
            .Select(Devices.InstanceId)
            .FirstOrDefault(id => id is not null && Devices.DeviceKey(id).Equals(deviceKey, StringComparison.OrdinalIgnoreCase));

    private static bool InContainer(string interfacePath, Guid container) =>
        Devices.InstanceId(interfacePath) is string id && Devices.ContainerId(id) == container;
}
