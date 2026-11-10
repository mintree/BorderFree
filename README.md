# BorderFree

## Download

Go to [Releases](https://github.com/mintree/BorderFree/releases/latest) and download `BorderFree-v1.1.1-win-x64.exe`, then run it. No install and no .NET needed, on Windows 10/11 64-bit. Because the EXE is unsigned, SmartScreen may show "Windows protected your PC"; click More info, then Run anyway. If a game runs as administrator, BorderFree must also run as administrator (right-click, Run as administrator), or Windows blocks it from moving that game's window. Settings are saved to `%AppData%\BorderFree`.

BorderFree puts any running window into a borderless frame on a 32:9 monitor. A lot of games still size themselves as 16:9 or 21:9, or open on the wrong display. Pick the window, pick a layout, and BorderFree strips the caption and moves it.

It runs on Windows 10 version 1809 (build 17763) and Windows 11. The window is per-monitor DPI aware (Per-Monitor V2), and every size is in physical pixels.

## Layouts

Presets are computed from the selected monitor. The primary display is selected until you change it under Advanced.

| Layout | What it does |
| --- | --- |
| **16:9 centered** | Largest 16:9 that fits, centered. On a 5120×1440 panel this is 2560×1440 at x = 1280. |
| **21:9 centered** | Largest exact 21:9 that fits, centered. On 5120×1440 this is 3360×1440. Some panels sold as "21:9" are actually 64:27; this preset uses 21:9. |
| **Full 32:9** | The whole selected display. On a 32:9 panel that is the full frame. On any other display it still fills that display. |
| **Left half / Right half** | The two halves of the display, tiled with no gap. On a true 32:9 panel each half is 16:9. Use them on two different programs to put those programs side by side. |

An odd width gives the extra pixel to the right half. Centered layouts that cannot fill the panel without cropping keep the extra pixel on the right or bottom.

By default the reference rectangle is the monitor bounds, so a borderless frame covers the panel edge to edge, including the taskbar area. Advanced has **Size from the work area** if you want the taskbar left uncovered.

## Use it

1. Start the game in windowed or borderless mode. BorderFree cannot change an exclusive fullscreen swap chain.
2. Select the game in the window list.
3. Click a layout.

That is the whole path. Clicking a layout applies it immediately. **Restore window borders** puts back the frame BorderFree removed during this run. Restore only knows windows changed since BorderFree was opened.

The status line under the window reports the pixel size and position, or why a move was refused.

### Remembered layouts

**Remember the last layout for each program** is on by default. The last preset, custom rectangle, and monitor are stored by executable file name (case-insensitive) in:

```text
%AppData%\BorderFree\settings.json
```

`C:\Games\Live\game.exe` and `game.exe` are the same profile. Two different games that ship the same file name share one profile. Turning the checkbox off stops reading and writing profiles. The file is kept, so turning it back on brings the memory back.

Selecting a remembered program fills its layout and monitor in the window. It does **not** move the window until you click a layout.

**When a remembered program opens, apply its layout once** is off by default, under Advanced. Turn it on if you want BorderFree to place a matching window by itself. It skips anything smaller than 320×240, so splash screens and most dialogs are left alone, and it applies once per window while that window is open. BorderFree has to be running. Auto-apply stays off unless you opt in, because opening a program should not move it on its own.

### Shortcuts

These are global while BorderFree is open, including when a game is in front. Change them under Advanced. A shortcut needs at least one of Ctrl, Alt, Shift, or the Windows key.

| Action | Default |
| --- | --- |
| Next layout | Ctrl+Alt+F11 |
| Previous layout | Ctrl+Alt+F10 |
| Reapply the current layout | Ctrl+Alt+F12 |

The cycle is 16:9 centered, 21:9 centered, full display, left half, right half, then back to the start. A hotkey targets the foreground window. If the foreground window is BorderFree itself, it targets the window selected in the list.

### Start with Windows

**Start with Windows** is off by default, under Advanced. Turning it on writes `BorderFree` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the quoted path of this EXE and `--minimized`. No administrator prompt is involved. The checkbox shows the registry entry itself, not only the saved setting. If you move the EXE, the next start rewrites that path while the setting stays on.

Windows then starts BorderFree hidden in the notification area, with hotkeys and auto-apply still running. Double-click the tray icon, or choose Open, to show the window. Reapply layout on that menu runs the same action as the reapply shortcut. Closing the window always hides BorderFree to the tray instead of quitting. Exit on the tray menu is what quits.

The Run key does not start BorderFree as administrator. A game that runs as administrator still needs BorderFree started with Run as administrator, or Windows blocks it from moving that game's window.

On a Windows PC, `scripts/verify-startup.ps1` reads the Run value, launches that command, and checks that BorderFree is running with no visible main window.

### Monitors

The monitor picker is under Advanced. Labels include the origin and the scale, for example `Primary — 5120×1440 at 0, 0 · 100%`. Layout math uses the physical pixels from the monitor, which is what `SetWindowPos` expects from a Per-Monitor V2 process.

Custom X, Y, width, and height are physical pixels on the virtual screen. A display to the left of the primary one has a negative X. Width and height must be from 1 to 16384.

### If a window jumps to another display

**If a placed window jumps to another monitor, move it back** is off by default, at the bottom of Advanced. Some games reposition themselves after a borderless change and land on a different display. With this on, BorderFree checks a couple of times a second and moves a window back only when `MonitorFromWindow` reports a different display from the one used for that placement. It only watches windows BorderFree has already placed in this session. It is off by default so a window you drag to another monitor stays there.

## Limitations

- **Exclusive fullscreen** has to be switched to windowed or borderless in the game first.
- **Elevated games** cannot be moved by a normal process. Windows blocks it (error 5). Restart BorderFree as administrator from Advanced, or start it elevated yourself. The app does not request administrator rights on every launch.
- Restore cannot recover a frame from a previous run of BorderFree.
- Global shortcuts only work while BorderFree is running. Closing the window hides it to the notification area and leaves them registered. Exit on the tray icon quits and unregisters them.

## Build

You need the .NET 8 SDK. On Windows, Visual Studio 2022 with the **.NET desktop development** workload is enough.

### Visual Studio

1. Open `BorderFree.sln`.
2. Set **BorderFree.App** as the startup project.
3. Build and run (F5).

### dotnet

From the repository root:

```bash
dotnet restore
dotnet test
dotnet build BorderFree.sln -c Release
dotnet run --project src/BorderFree.App/BorderFree.App.csproj
```

The Release build writes the program to:

```text
src/BorderFree.App/bin/Release/net8.0-windows10.0.17763.0/BorderFree.exe
```

That build uses the shared .NET 8 Desktop Runtime. To publish a folder you can copy to another PC:

```bash
dotnet publish src/BorderFree.App/BorderFree.App.csproj -c Release -r win-x64 --self-contained false
```

Add `--self-contained true` if the other PC does not have the .NET 8 Desktop Runtime. Publish for `win-x64`, `win-x86`, or `win-arm64`.

`BorderFree.Core` and `BorderFree.Platform` are built with `EnableWindowsTargeting`, so `dotnet test` and `dotnet build` also run on Linux and macOS. The app still has to be launched on Windows. Win32 calls are not exercised by the unit tests.

## Tests

Geometry, the preset cycle, hotkey parsing, and the settings file are covered by `tests/BorderFree.Core.Tests`. From the repository root:

```bash
dotnet test
```

## Settings file

`%AppData%\BorderFree\settings.json` is indented JSON. A missing file means the defaults. If the file cannot be parsed, it is copied to `settings.json.bak` and replaced with defaults. An unreadable file must not stop the app from opening.

```json
{
  "version": 2,
  "rememberPerProgram": true,
  "autoApplyOnDetect": false,
  "safeguardWrongMonitor": false,
  "useWorkArea": false,
  "startWithWindows": false,
  "monitorDeviceName": "\\\\.\\DISPLAY1",
  "hotkeys": {
    "nextPreset": "Ctrl+Alt+F11",
    "previousPreset": "Ctrl+Alt+F10",
    "reapply": "Ctrl+Alt+F12"
  },
  "programs": {
    "game.exe": {
      "preset": "Centered16x9",
      "custom": null,
      "monitorDeviceName": "\\\\.\\DISPLAY1"
    }
  }
}
```

`preset` is one of `Centered16x9`, `Centered21x9`, `Full32x9`, `LeftHalf`, `RightHalf`, or `Custom`. A custom layout stores `custom` as `{ "x", "y", "width", "height" }`. A version 1 file has no `startWithWindows` field; it still loads, and that option stays off until you turn it on.

## Project layout

```text
BorderFree.sln
src/BorderFree.Core/        preset math, hotkeys, settings (no Win32)
src/BorderFree.Platform/    Win32 window, monitor, and hotkey calls
src/BorderFree.App/         WPF window
scripts/verify-startup.ps1  Windows check for the Run key and a hidden start
tests/BorderFree.Core.Tests/
```

## Source

The public repository is <https://github.com/mintree/BorderFree>.

If this checkout does not already have that remote:

```bash
git remote add origin https://github.com/mintree/BorderFree.git
```

## License

MIT. See [LICENSE](LICENSE).
