# OP1w 4K v2 Battery Tray Icon

Windows tray icon that shows the battery percentage for the Endgame Gear OP1w 4K v2, and for other mice that report their battery to Windows.

## Features

- Battery percentage shown directly in the tray icon
- Green at 50-100%, yellow at 20-49%, red at 0-19%
- Grey with the last known value when the mouse is off or asleep for more than 5 minutes
- Polls once per minute
- Right-click menu: Choose mouse, Run at startup, Refresh now, Exit
- Left-click opens the official Endgame Gear config tool if it is placed beside the app

## Choosing a mouse

Right-click the tray icon and pick "Choose mouse...". Click the left button and then the right button of the mouse you want to track. The app finds which device sent the clicks and saves it to `%APPDATA%\OP1wBatteryTray\mouse.json`.

On first start the window opens by itself if no OP1w is connected.

## Supported devices

- Endgame Gear OP1w 4K v2, through its vendor protocol
  - Dock/dongle: VID_3367&PID_1970
  - Wired fallback: VID_3367&PID_1984
- Bluetooth mice whose battery shows in Windows Settings > Bluetooth & devices
- USB or wireless mice that expose a standard HID battery value (Battery Strength or State of Charge)

Mice that use a brand-only protocol (for example Logitech HID++, Razer, SteelSeries) are not supported. The setup window says so when it cannot read a value.

## Build

Requires Windows and the .NET 8 SDK.

```powershell
dotnet publish . -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```
