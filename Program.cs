using System.Threading;
using System.Windows.Forms;

namespace WirelessMouseBatteryTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase)))
        {
            var reading = BatteryReader.TryRead(MouseChoice.Load());
            if (reading is null)
            {
                Console.Error.WriteLine("Battery not available.");
                return 1;
            }

            Console.WriteLine($"{reading.Percent}% ({reading.DeviceName ?? "mouse"}, {reading.Source}, {reading.Power}, {reading.VoltageMillivolts?.ToString() ?? "unknown"} mV)");
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--diag", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in Report.Lines())
                Console.WriteLine(line);
            return 0;
        }

        ApplicationConfiguration.Initialize();

        if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Installer.Uninstall();
            return 0;
        }

        if (Installer.CanInstall && Installer.OfferInstall()) return 0;

        using var mutex = new Mutex(true, "WirelessMouseBatteryTray.Singleton", out var created);
        if (!created) return 0;

        Application.Run(new TrayAppContext());
        return 0;
    }
}
