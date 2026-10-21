using System.Runtime.InteropServices;
using static WirelessMouseBatteryTray.Native;

namespace WirelessMouseBatteryTray;

// Identifies the mouse by which raw input device sends a left click, then a right click.
internal sealed class MouseSetupForm : Form
{
    private readonly Label _message;
    private readonly Button _save;
    private IntPtr _leftClickDevice;
    private bool _done;

    public MouseChoice? Choice { get; private set; }

    public MouseSetupForm()
    {
        Text = "Choose your mouse";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 260);
        Font = new Font("Segoe UI", 12f);

        _message = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(16) };

        _save = new Button { Text = "Save", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        var restart = new Button { Text = "Start again", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        restart.Click += (_, _) => Restart();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8)
        };
        buttons.Controls.AddRange([cancel, _save, restart]);

        Controls.Add(_message);
        Controls.Add(buttons);
        CancelButton = cancel;
        Restart();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var device = new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = Handle };
        RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT && !_done) OnRawInput(m.LParam);
        base.WndProc(ref m);
    }

    private void Restart()
    {
        _done = false;
        _leftClickDevice = IntPtr.Zero;
        Choice = null;
        _save.Enabled = false;
        _message.Text = "Click the LEFT button of the mouse you want to track.\nClick inside this window.";
    }

    private void OnRawInput(IntPtr rawInput)
    {
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(rawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size < headerSize + 8) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInput, RID_INPUT, buffer, ref size, headerSize) != size) return;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.Type != RIM_TYPEMOUSE) return;

            // RAWMOUSE: usFlags (2 bytes) + padding (2 bytes), then usButtonFlags.
            var buttons = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + 4);
            if ((buttons & (RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_RIGHT_BUTTON_DOWN)) == 0) return;

            if (header.Device == IntPtr.Zero)
            {
                _leftClickDevice = IntPtr.Zero;
                _message.Text = "That click did not come from a mouse.\nClick the LEFT button of your mouse.";
                return;
            }

            if (_leftClickDevice == IntPtr.Zero)
            {
                if ((buttons & RI_MOUSE_LEFT_BUTTON_DOWN) == 0) return;
                _leftClickDevice = header.Device;
                _message.Text = "Now click the RIGHT button of the same mouse.";
                return;
            }

            if ((buttons & RI_MOUSE_RIGHT_BUTTON_DOWN) == 0) return;
            if (header.Device != _leftClickDevice)
            {
                _leftClickDevice = IntPtr.Zero;
                _message.Text = "That click came from a different device.\nClick the LEFT button of your mouse again.";
                return;
            }

            _done = true;
            Identify(header.Device);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private async void Identify(IntPtr device)
    {
        uint length = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, null, ref length);
        var chars = new char[length];
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, chars, ref length);
        var path = new string(chars).TrimEnd('\0');
        var instanceId = Devices.InstanceId(path);
        if (instanceId is null)
        {
            _done = false;
            _leftClickDevice = IntPtr.Zero;
            _message.Text = "Windows did not identify this mouse.\nClick the LEFT button to try again.";
            return;
        }

        var key = Devices.DeviceKey(instanceId);
        var choice = new MouseChoice(key, key.Contains(Op1w.VendorKey, StringComparison.OrdinalIgnoreCase) ? "OP1w" : Devices.Name(path, instanceId));
        _message.Text = $"Found: {choice.Name}\nReading the battery...";

        BatteryReading? reading = null;
        try
        {
            reading = await Task.Run(() => BatteryReader.TryRead(choice));
        }
        catch
        {
            // Treated the same as "no battery value".
        }

        if (IsDisposed || Choice is not null || !_done) return;
        Choice = choice;
        _save.Enabled = true;
        _message.Text = reading is null
            ? $"Found: {choice.Name}\nNo battery value from this mouse.\nIf the mouse is on, this app cannot read its battery.\nYou can save it anyway."
            : $"Found: {choice.Name}\nBattery: {reading.Percent}%\nClick Save to track this mouse.";
    }
}
