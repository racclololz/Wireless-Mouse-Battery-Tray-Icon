using System.Diagnostics;
using Microsoft.Win32;

namespace WirelessMouseBatteryTray;

// Per-user install (no admin): %LOCALAPPDATA%\Programs, Start Menu shortcut, startup entry, Settings > Apps entry.
internal static class Installer
{
    public const string DisplayName = "Wireless Mouse Battery Tray";
    private const string ExeName = "WirelessMouseBatteryTray.exe";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WirelessMouseBatteryTray";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", DisplayName);
    private static readonly string InstalledExe = Path.Combine(InstallDir, ExeName);
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), DisplayName + ".lnk");

    // Only the single-file release exe can copy itself; a dev build's exe needs the dll beside it.
    public static bool CanInstall => string.IsNullOrEmpty(typeof(Installer).Assembly.Location) && !IsInstalledCopy;

    private static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    // Returns true when this process should exit (it installed and started the installed copy).
    public static bool OfferInstall()
    {
        var update = File.Exists(InstalledExe);
        var answer = MessageBox.Show(
            update
                ? $"Update {DisplayName} to version {Version}?"
                : $"Install {DisplayName}?\n\nIt shows your mouse battery in the taskbar and starts with Windows.\nYou can remove it later in Settings > Apps.\n\nClick No to run it once without installing.",
            DisplayName,
            update ? MessageBoxButtons.YesNo : MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (answer == DialogResult.Cancel) return true;
        if (answer == DialogResult.No) return false;

        try
        {
            StopOtherInstances();
            Directory.CreateDirectory(InstallDir);
            File.Copy(Environment.ProcessPath!, InstalledExe, true);
            CreateShortcut();
            using (var run = Registry.CurrentUser.CreateSubKey(TrayAppContext.RunKeyPath, true))
                run.SetValue(TrayAppContext.RunValueName, $"\"{InstalledExe}\"", RegistryValueKind.String);
            WriteUninstallEntry();
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
        }
        catch (Exception error)
        {
            AppLog.Write($"install failed: {error}");
            MessageBox.Show($"Install failed:\n{error.Message}", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return true;
    }

    public static void Uninstall()
    {
        if (MessageBox.Show($"Remove {DisplayName}?", DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        StopOtherInstances();
        using (var run = Registry.CurrentUser.OpenSubKey(TrayAppContext.RunKeyPath, true))
            run?.DeleteValue(TrayAppContext.RunValueName, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);

        // The running exe cannot delete itself; a hidden shell removes the folder after this process exits.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string Version =>
        typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "";

    private static void StopOtherInstances()
    {
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            using (process)
            {
                if (process.Id == self) continue;
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // Already gone or not ours; the copy below reports a real failure.
                }
            }
        }
    }

    private static void CreateShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        var shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = InstalledExe;
        shortcut.WorkingDirectory = InstallDir;
        shortcut.Description = DisplayName;
        shortcut.Save();
    }

    private static void WriteUninstallEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, true);
        key.SetValue("DisplayName", DisplayName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }
}
