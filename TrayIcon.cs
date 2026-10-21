using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WirelessMouseBatteryTray;

// Draws the tray number at the exact pixel size the taskbar uses, so nothing is lost to downscaling.
internal static class TrayIcon
{
    private const int SM_CXSMICON = 49;
    private static readonly string DigitFont = PickFont("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI");

    public static int TaskbarIconSize()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var dpi = taskbar != IntPtr.Zero ? GetDpiForWindow(taskbar) : 0u;
        var size = dpi > 0 ? GetSystemMetricsForDpi(SM_CXSMICON, dpi) : SystemInformation.SmallIconSize.Width;
        return Math.Max(16, size);
    }

    public static Icon Create(int? percent, Color fill, int size)
    {
        using var bitmap = Render(percent, fill, size);
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

    internal static Bitmap Render(int? percent, Color fill, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using (var background = RoundedRect(new RectangleF(0, 0, size, size), Math.Max(2f, size / 8f)))
        using (var fillBrush = new SolidBrush(fill))
            g.FillPath(fillBrush, background);

        var label = percent is null ? "?" : percent.Value.ToString();
        using var text = new GraphicsPath();
        using var family = new FontFamily(DigitFont);
        text.AddString(label, family, (int)FontStyle.Bold, size * 4f, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = text.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return bitmap;

        // Digits fill the full height; three digits are narrowed instead of shrunk.
        var padX = Math.Max(1f, size * 0.06f);
        var padY = Math.Max(1f, size * 0.12f);
        var scaleY = (size - 2 * padY) / bounds.Height;
        var scaleX = Math.Min(scaleY, (size - 2 * padX) / bounds.Width);
        using var matrix = new Matrix();
        matrix.Translate(size / 2f, size / 2f);
        matrix.Scale(scaleX, scaleY);
        matrix.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
        text.Transform(matrix);

        using var textBrush = new SolidBrush(Luminance(fill) > 0.6 ? Color.Black : Color.White);
        g.FillPath(textBrush, text);
        return bitmap;
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static string PickFont(params string[] names)
    {
        using var installed = new InstalledFontCollection();
        return names.FirstOrDefault(n => installed.Families.Any(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) ?? FontFamily.GenericSansSerif.Name;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
