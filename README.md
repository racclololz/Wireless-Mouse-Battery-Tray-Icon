# Wireless Mouse Battery Tray Icon

A small Windows app that shows your wireless mouse battery as a number in the taskbar, next to the clock.

## Install

1. Download `WirelessMouseBatteryTray.exe` from the [latest release](https://github.com/racclololz/Wireless-Mouse-Battery-Tray-Icon/releases/latest).
2. Double-click it and click **Yes** to install. No admin rights are needed, and the app starts with Windows from then on.
3. If the app does not find a supported mouse, a window opens. Click the left button and then the right button of your mouse inside it, then click **Save**.

To update, download the new version and double-click it. To remove the app, go to **Settings > Apps > Installed apps**, find **Wireless Mouse Battery Tray** and click **Uninstall**.

## Using it

- Green: 50-100%. Yellow: 20-49%. Red: 0-19%.
- Blue: the mouse is charging or plugged in.
- Grey: the mouse is off or asleep. The number is the last value the app read.
- `?`: the app has not read a value yet.
- Hover over the icon to see the mouse name, the battery and, after some use, about how long it will last.
- Right-click the icon for **Choose mouse...**, **Alert at 15%** (off until you turn it on), **Run at startup**, **Refresh now**, **Create report...** and **Exit**.

The app checks the battery once per minute, and a few seconds after you plug in or unplug a cable or dongle.

## Which mice work

- **Logitech** mice on a Unifying, Bolt or Lightspeed receiver, on a cable, or on Bluetooth.
- **Endgame Gear** wireless mice, wireless or on their cable.
- **Bluetooth mice** that show their battery in Windows under **Settings > Bluetooth & devices**.
- **Other mice** that report battery the standard way (HID Battery Strength or State of Charge).

If your mouse does not work, right-click the icon, click **Create report...** and send the file from your Desktop to the developer.

Mice that use the vendor feature pattern can be added in `devices.json` without new code.

## For developers

Requires Windows and the .NET 8 SDK.

Build the single-file exe that goes on the release page:

```powershell
dotnet publish WirelessMouseBatteryTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Run the tests:

```powershell
dotnet run --project tests/BatteryTests.csproj
```

Command-line options: `--once` prints one reading, `--diag` prints what the app finds for the chosen mouse, and `--uninstall` removes the installed app.

Settings are in `%APPDATA%\WirelessMouseBatteryTray\mouse.json`. The log is in `%LOCALAPPDATA%\WirelessMouseBatteryTray\battery.log`.
