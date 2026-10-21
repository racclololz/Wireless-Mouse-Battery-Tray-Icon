using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WirelessMouseBatteryTray;

internal sealed class TrayAppContext : ApplicationContext
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunValueName = "WirelessMouseBatteryTray";
    private const int IconSize = 128;
    private const int PollIntervalMs = 60_000;
    private const int StaleAfterMs = 5 * PollIntervalMs;

    private static readonly Color Grey = Color.FromArgb(120, 120, 120);
    private static readonly Color Blue = Color.FromArgb(0, 120, 215);

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly ToolStripMenuItem _alertMenuItem;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly BatteryTracker _tracker = new();
    private Icon? _currentIcon;
    private (int? Percent, Color Fill)? _displayed;
    private DateTime _lastReadUtc = DateTime.MinValue;
    private bool _refreshInProgress;
    private MouseChoice? _mouse = MouseChoice.Load();
    private AppSettings _settings = AppSettings.Load();
    private MouseSetupForm? _setupForm;
    private string? _deviceName;
    private readonly DeviceChangeWindow _deviceWatcher;
    private int _deviceChangeVersion;

    private string MouseName => _deviceName ?? _mouse?.Name ?? "Mouse";

    public TrayAppContext()
    {
        _startupMenuItem = new ToolStripMenuItem("Run at startup")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        _startupMenuItem.CheckedChanged += (_, _) => SetStartupEnabled(_startupMenuItem.Checked);

        _alertMenuItem = new ToolStripMenuItem($"Alert at {BatteryTracker.LowThreshold}%")
        {
            CheckOnClick = true,
            Checked = _settings.LowBatteryAlert
        };
        _alertMenuItem.CheckedChanged += (_, _) =>
        {
            _settings = _settings with { LowBatteryAlert = _alertMenuItem.Checked };
            _settings.Save();
        };

        var chooseMouseItem = new ToolStripMenuItem("Choose mouse...");
        chooseMouseItem.Click += (_, _) => ShowMouseSetup();

        var refreshItem = new ToolStripMenuItem("Refresh now");
        refreshItem.Click += async (_, _) => await RefreshWithoutStoppingPollLoopAsync();

        var reportItem = new ToolStripMenuItem("Create report...");
        reportItem.Click += async (_, _) => await CreateReportAsync();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        _notifyIcon = new NotifyIcon
        {
            Text = $"{MouseName}: checking...",
            Icon = CreateIcon(null, Grey),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _notifyIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            chooseMouseItem, _alertMenuItem, _startupMenuItem, refreshItem, reportItem, new ToolStripSeparator(), exitItem
        });
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenConfigurationTool();
        };

        _deviceWatcher = new DeviceChangeWindow(OnDevicesChanged);
        _ = PollLoopAsync(_shutdown.Token);
    }

    // Cable in/out and dongle changes re-enumerate devices; read again shortly after instead of waiting for the next poll.
    private async void OnDevicesChanged()
    {
        var version = ++_deviceChangeVersion;
        await Task.Delay(2500);
        for (var wait = 0; _refreshInProgress && wait < 15; wait++) await Task.Delay(1000);
        if (version == _deviceChangeVersion && !_shutdown.IsCancellationRequested) await RefreshWithoutStoppingPollLoopAsync();
    }

    private sealed class DeviceChangeWindow : NativeWindow
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private readonly Action _changed;

        public DeviceChangeWindow(Action changed)
        {
            _changed = changed;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE) _changed();
            base.WndProc(ref m);
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        if (!MouseChoice.Exists && await Task.Run(() => BatteryReader.TryRead((MouseChoice?)null)) is null) ShowMouseSetup();
        await RefreshWithoutStoppingPollLoopAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshWithoutStoppingPollLoopAsync();
        }
    }

    private async Task RefreshWithoutStoppingPollLoopAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception error)
        {
            AppLog.Write($"refresh failed: {error}");
            _refreshInProgress = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress) return;
        _refreshInProgress = true;

        try
        {
            var mouse = _mouse;
            var reading = await Task.Run(() => ReadWithRetries(mouse));
            if (mouse != _mouse) return;
            var now = DateTime.UtcNow;

            if (reading is null)
            {
                if (_tracker.Percent is not int last)
                {
                    UpdateIcon(null, Grey, $"{MouseName}: no battery value");
                    return;
                }

                var age = now - _lastReadUtc;
                if (age < TimeSpan.FromMilliseconds(StaleAfterMs)) return;

                // Grey icon keeps the last real value so an asleep/off mouse is not shown as unknown.
                UpdateIcon(last, Grey, $"{MouseName}: off or asleep (last {last}%, {FormatAge(age)} ago)");
                return;
            }

            _deviceName = reading.DeviceName ?? _deviceName;
            _lastReadUtc = now;
            var percent = _tracker.Add(reading, now);
            AppLog.Write($"fresh reading: {reading.Percent}% shown {percent}% ({reading.Source}, {reading.Power}, {reading.VoltageMillivolts} mV)");

            var (fill, status) = _tracker.Power switch
            {
                PowerState.Charging => (Blue, $"{percent}%, charging"),
                PowerState.PluggedFull => (Blue, $"{percent}%, plugged in"),
                _ => (BatteryColor(percent), _tracker.TimeLeft(now) is { } left ? $"{percent}%, {BatteryTracker.Format(left)}" : $"{percent}%")
            };
            UpdateIcon(percent, fill, $"{MouseName}: {status}");

            if (_settings.LowBatteryAlert && _tracker.TakeLowBatterySignal())
                _notifyIcon.ShowBalloonTip(10_000, "Mouse battery low", $"{MouseName} is at {percent}%.", ToolTipIcon.Warning);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private static Color BatteryColor(int percent) =>
        percent < 20 ? Color.FromArgb(220, 45, 45)
        : percent < 50 ? Color.FromArgb(230, 170, 30)
        : Color.FromArgb(35, 165, 90);

    private static string FormatAge(TimeSpan age) =>
        age.TotalHours >= 1 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalMinutes} min";

    private async Task CreateReportAsync()
    {
        try
        {
            var path = await Task.Run(Report.WriteToDesktop);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception error)
        {
            AppLog.Write($"report failed: {error}");
            MessageBox.Show($"Could not create the report:\n{error.Message}", Installer.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowMouseSetup()
    {
        if (_setupForm is not null)
        {
            _setupForm.Activate();
            return;
        }

        using var form = new MouseSetupForm();
        _setupForm = form;
        try
        {
            if (form.ShowDialog() != DialogResult.OK || form.Choice is null) return;
            form.Choice.Save();
            _mouse = form.Choice;
            _deviceName = null;
            _tracker.Reset();
            _lastReadUtc = DateTime.MinValue;
            _refreshInProgress = false;
        }
        finally
        {
            _setupForm = null;
        }

        _ = RefreshWithoutStoppingPollLoopAsync();
    }

    private static BatteryReading? ReadWithRetries(MouseChoice? mouse)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            BatteryReading? reading = null;
            try
            {
                reading = BatteryReader.TryRead(mouse);
            }
            catch
            {
                // Device reconnects can cause transient HID/setupapi failures.
            }

            if (reading is not null) return reading;
            Thread.Sleep(750);
        }

        return null;
    }

    private void UpdateIcon(int? percent, Color fill, string text)
    {
        var displayChanged = _displayed != (percent, fill);
        if (fill == Grey) AppLog.Write($"display: {text}");
        var oldIcon = _currentIcon;
        _currentIcon = CreateIcon(percent, fill);
        _notifyIcon.Icon = _currentIcon;
        _notifyIcon.Text = text.Length <= 127 ? text : text[..127];

        if (displayChanged && _notifyIcon.Visible)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
        }

        _displayed = (percent, fill);
        oldIcon?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        _shutdown.Cancel();
        _deviceWatcher.DestroyHandle();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        base.ExitThreadCore();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(RunValueName) as string;
        return string.Equals(Unquote(value), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            key.SetValue(RunValueName, Quote(Application.ExecutablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(RunValueName, false);
        }
    }

    private static string Quote(string path) => $"\"{path}\"";

    private static string? Unquote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    // Opens a maker's configuration tool (named in devices.json) if it was placed beside the app.
    private static void OpenConfigurationTool()
    {
        var toolPath = DeviceList.VendorFeature
            .Select(d => d.ConfigTool)
            .OfType<string>()
            .Select(name => Path.Combine(AppContext.BaseDirectory, name))
            .FirstOrDefault(File.Exists);
        if (toolPath is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = toolPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // Keep the tray app battery-only if the optional vendor tool fails to launch.
        }
    }

    private static Icon CreateIcon(int? percent, Color fill)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using var fillBrush = new SolidBrush(fill);
        using var path = RoundedRect(new Rectangle(0, 0, IconSize, IconSize), 18);
        g.FillPath(fillBrush, path);

        var label = percent is null ? "?" : percent.Value.ToString();
        using var font = CreateFittedFont(g, label);
        using var textBrush = new SolidBrush(Color.White);
        using var outlineBrush = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var textRect = new RectangleF(-6, -11, IconSize + 12, IconSize + 18);
        DrawTextOutline(g, label, font, outlineBrush, textRect, format);
        g.DrawString(label, font, textBrush, textRect, format);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Font CreateFittedFont(Graphics g, string label)
    {
        var size = label.Length >= 3 ? 74f : 104f;
        while (size > 28f)
        {
            var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(label, font);
            if (measured.Width <= IconSize + 12f && measured.Height <= IconSize + 4f) return font;
            font.Dispose();
            size -= 1f;
        }

        return new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static void DrawTextOutline(Graphics g, string label, Font font, Brush brush, RectangleF textRect, StringFormat format)
    {
        const float outline = 4f;
        var offsets = new[]
        {
            new PointF(-outline, 0),
            new PointF(outline, 0),
            new PointF(0, -outline),
            new PointF(0, outline),
            new PointF(-outline, -outline),
            new PointF(outline, -outline),
            new PointF(-outline, outline),
            new PointF(outline, outline)
        };

        foreach (var offset in offsets)
        {
            g.DrawString(label, font, brush, new RectangleF(
                textRect.X + offset.X,
                textRect.Y + offset.Y,
                textRect.Width,
                textRect.Height), format);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
