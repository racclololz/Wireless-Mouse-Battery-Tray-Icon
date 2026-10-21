using System.Threading;
using System.Windows.Forms;

namespace OP1wBatteryTray;

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

            Console.WriteLine($"{reading.Percent}% ({reading.Source}, {reading.VoltageMillivolts?.ToString() ?? "unknown"} mV)");
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--diag", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in BatteryReader.Diagnose(MouseChoice.Load()))
                Console.WriteLine(line);
            return 0;
        }

        using var mutex = new Mutex(true, "OP1wBatteryTray.Singleton", out var created);
        if (!created) return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
        return 0;
    }
}
