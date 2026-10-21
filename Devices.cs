using System.Text;
using static OP1wBatteryTray.Native;

namespace OP1wBatteryTray;

internal static class Devices
{
    public static readonly Guid HidInterface = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    public static readonly Guid MouseInterface = new("378de44c-56ef-11d1-bc8c-00a0c91405dd");

    // Every devnode of one physical device (or one dongle) shares this; built-in devices share the "this PC" container.
    public static readonly Guid LocalMachineContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    private static DEVPROPKEY _instanceIdKey = new("78c34fc8-104a-4aca-9ea4-524d52996e57", 256);
    private static DEVPROPKEY _containerIdKey = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
    private static DEVPROPKEY _friendlyNameKey = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    private static DEVPROPKEY _busDescKey = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);
    // Battery level Windows reads from Bluetooth devices (the value shown in Settings > Bluetooth).
    private static DEVPROPKEY _bluetoothBatteryKey = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);

    public static string[] Interfaces(Guid interfaceClass)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out var length, ref interfaceClass, null, 0) != CR_SUCCESS) return [];
            var buffer = new char[length];
            var result = CM_Get_Device_Interface_ListW(ref interfaceClass, null, buffer, length, 0);
            if (result == CR_BUFFER_SMALL) continue;
            if (result != CR_SUCCESS) return [];
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        return [];
    }

    public static string[] PresentInstanceIds()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out var length, null, CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS) return [];
            var buffer = new char[length];
            var result = CM_Get_Device_ID_ListW(null, buffer, length, CM_GETIDLIST_FILTER_PRESENT);
            if (result == CR_BUFFER_SMALL) continue;
            if (result != CR_SUCCESS) return [];
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        return [];
    }

    public static string? InstanceId(string interfacePath)
    {
        var data = InterfaceProperty(interfacePath, ref _instanceIdKey);
        return data is null ? null : Encoding.Unicode.GetString(data).TrimEnd('\0');
    }

    public static Guid? ContainerId(string instanceId)
    {
        var data = DevNodeProperty(instanceId, ref _containerIdKey);
        return data is { Length: 16 } ? new Guid(data) : null;
    }

    public static int? BluetoothBattery(string instanceId)
    {
        var data = DevNodeProperty(instanceId, ref _bluetoothBatteryKey);
        return data is { Length: 1 } && data[0] <= 100 ? data[0] : null;
    }

    // "HID\VID_3367&PID_1970&MI_00\8&E8AEDBA&0&0000" -> "HID\VID_3367&PID_1970&MI_00": stable across reboots; Bluetooth keys include the address.
    public static string DeviceKey(string instanceId)
    {
        var cut = instanceId.LastIndexOf('\\');
        return cut > 0 ? instanceId[..cut] : instanceId;
    }

    public static string Name(string interfacePath, string instanceId)
    {
        using (var handle = OpenHid(interfacePath, 0))
        {
            var buffer = new byte[256];
            if (!handle.IsInvalid && HidD_GetProductString(handle, buffer, buffer.Length))
            {
                var product = Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                if (product.Length > 0) return product;
            }
        }

        foreach (var key in new[] { _busDescKey, _friendlyNameKey })
        {
            var k = key;
            var data = DevNodeProperty(instanceId, ref k);
            if (data is not null) return Encoding.Unicode.GetString(data).TrimEnd('\0');
        }

        return "Mouse";
    }

    private static byte[]? InterfaceProperty(string interfacePath, ref DEVPROPKEY key)
    {
        uint size = 0;
        CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, null, ref size, 0);
        if (size == 0) return null;
        var buffer = new byte[size];
        return CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, buffer, ref size, 0) == CR_SUCCESS ? buffer : null;
    }

    private static byte[]? DevNodeProperty(string instanceId, ref DEVPROPKEY key)
    {
        if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CR_SUCCESS) return null;
        uint size = 0;
        CM_Get_DevNode_PropertyW(devInst, ref key, out _, null, ref size, 0);
        if (size == 0) return null;
        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) == CR_SUCCESS ? buffer : null;
    }
}
