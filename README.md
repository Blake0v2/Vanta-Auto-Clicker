# Vanta Auto Clicker

A native Windows desktop autoclicker with pure black main surfaces, neutral text and controls, light-blue outlines, default and advanced views, and the supplied Vanta logo. Its actual window uses transparent rounded corners, with matching clipping on the window contents. Paytone One is embedded throughout the UI, with smooth view transitions and window resizing. Built with C# / WPF and Windows `SendInput`. The executable contains its UI, artwork, and font; it does not need a separate asset folder, font installation, or development runtime.

## Run

For a normal Windows installation, run **`dist/Vanta.Auto.Clicker.Setup.exe`** once. It installs for the current user without administrator access, adds Start menu and optional desktop shortcuts, and registers Vanta under Windows **Installed apps** with its own uninstaller. The installed files live in `%LOCALAPPDATA%\Programs\Vanta Auto Clicker`.

For portable use, open **`dist/Vanta Auto Clicker.exe`**, or extract the versioned ZIP from `dist`. Both versions require Windows 10 / 11 and .NET Framework 4.8 or newer. Python, Node.js, and a .NET SDK are not required. Tested locally on Windows 11; Windows 10 and additional hardware / display configurations have not been independently tested.

Move the cursor to your target and press **F8** to toggle clicking. **Esc** stops an active run. The Start button provides a two-second positioning countdown. Open **Test pad** to try real clicks safely. The app stays idle on launch.

See [QUICKSTART.txt](QUICKSTART.txt) for the full user guide.

## Features

| Feature | Behavior |
| --- | --- |
| Default view | Cadence, shortcut, toggle/hold, mouse button, duration, variation |
| Advanced view | All controls plus limits, double clicks, ordered sequences |
| Cadence | Rate or delay; milliseconds, seconds, minutes, hours; 1 ms–24 hours |
| Global shortcut | Customizable; key repeat is ignored; unrelated shortcuts pass through |
| Hold | Starts on press and stops when the key or a required modifier is released |
| Mouse button | Left, middle, right |
| Duration | 0–100% button-down duration per cycle; cancellation releases the button |
| Speed variation | Uniformly varies cycle intervals by ±0–90%, never below 1 ms |
| Limits | Exact individual-click count, including odd counts in double-click mode, or elapsed seconds |
| Double click | Two clicks per cycle, both at the same point; configurable 1–5,000 ms release-to-press gap |
| Sequence | Up to 1,000 physical desktop positions; F6 capture, countdown capture, reorder, remove, loop |
| Preferences | Atomic local XML save, backup, import/export, reset, always on top, minimize on start |
| Safety | Esc, stop, exit, lock, suspend; own-window protection; visible injection errors |
| Test pad | Receives and counts actual left, middle, and right clicks |

Both views edit the same settings. Advanced options remain active when switching to Default, and the footer summarizes enabled limits and sequences. Settings are locked during a run; stop before editing.

### Timing semantics

The cadence describes **cycles**, not guaranteed delivered clicks. A single-click cycle contains one click; a double-click cycle contains two. Each cycle selects its next sequence point once. Double-click duration uses the remaining interval after the gap, split between the two presses. If the gap exceeds the interval, the cycle takes longer. A 100% duration still releases between clicks. Zero duration sends a press followed by a release without a scheduled hold.

Time limits start after the positioning countdown and interrupt a held button or a long interval. Click limits count completed press/release pairs, not cycle count. Stopping during a press releases and counts that press. The engine uses cancellable waits and a scoped 1 ms Windows timer request; it does not busy-spin. Windows scheduling and the target app determine effective speed.

Modifiers held in a custom shortcut can affect the destination app. An unmodified function key such as F8 is recommended. F6 is reserved for capture; Esc is reserved for stopping. Injected keyboard events from other automation are intentionally ignored. The dedicated global hook does not log keys, write keystrokes, or transmit data.

## Build and test

Run in Windows PowerShell from this directory:

```powershell
.\build.ps1 -Test
.\build.ps1 -UiTest
.\scripts\Inspect-UI.ps1
.\scripts\Release.ps1
```

The build uses the compiler bundled with .NET Framework, so there are no NuGet or npm dependencies. If the local PowerShell policy prevents running downloaded scripts, review the scripts and follow your organization's approved process; do not change machine-wide policy just to build.

`-Test` runs engine, settings, hotkey-state, and interop-layout checks without injecting input. `-UiTest` briefly opens desktop windows, renders the views into `artifacts`, and sends a bounded number of real clicks into an owned test pad. It restores the cursor and closes the windows afterward. Run UI tests only in an interactive Windows session, with the desktop available and no other automation active. Unit tests can run on CI.

`Inspect-UI.ps1` inspects the actual executable using Windows UI Automation. It checks native window corners, intermediate animated window sizes, and rapid navigation, switches the three views, opens the test pad and font license, and takes screenshots against a neutral backdrop without starting the click engine. It restores the original view and closes its own app instance. `-CompileTests` builds the tests without running them; `-OutputDirectory` builds the app to a separate folder while an existing copy is open.

Source layout:

- `src/ClickEngine.cs`: isolated worker, cadence, cancellation, limits, and sequences.
- `src/Native.cs`: Windows input, self-click protection, dedicated global keyboard hook.
- `src/Config.cs`: validated settings and safe XML persistence.
- `src/ViewModel.cs`: shared default/advanced settings bindings.
- `src/App.cs`, `src/MainWindow.xaml`, `src/Theme.xaml`: application UI, shared rounded controls, profiles, safety lifecycle, test pad.
- `src/RoundedWindow.cs`: content clipping that stays aligned with transparent window corners.
- `src/UiMotion.cs`: interruptible view and window animations that follow Windows animation preferences.
- `installer/Installer.cs`: native per-user setup and uninstall UI, Start menu shortcuts, and Windows Installed apps registration.
- `scripts/Build-Installer.ps1`: builds the self-contained setup executable with the app and documentation embedded.
- `assets/fonts/`: original Paytone One font and SIL Open Font License; the build embeds the font in WPF resources and exposes the license in Settings.
- `tests/Tests.cs`: engine tests and real desktop integration tests.
- `scripts/Make-Icon.ps1`: converts the supplied logo to a multi-resolution Windows icon.

## Share and publish

`scripts/Release.ps1` builds and tests, then produces:

- `dist/Vanta Auto Clicker.exe` — standalone executable.
- `dist/Vanta.Auto.Clicker.Setup.exe` — recommended installer with Start menu shortcuts and uninstall support.
- `dist/Vanta-Auto-Clicker-1.0.4-win.zip` — executable, user guide, and font license.
- `dist/SHA256SUMS.txt` — hashes of the installer, portable executable, and ZIP.

Creating a tag matching the app version, such as `v1.0.4`, builds the app and installer, runs unit tests, checks that the website previews match the app source, and attaches the Setup EXE, portable EXE, ZIP, and checksums to that GitHub Release. You can also upload those files manually to a release or a web host you control. The website download button should point to the Setup EXE for the normal installed experience.

Installed builds have a manual **Check for updates** button in Settings. It reads the latest non-prerelease GitHub Release, downloads the exact `Vanta.Auto.Clicker.Setup.exe` asset only after confirmation, verifies its GitHub SHA-256 digest (or the matching entry in `SHA256SUMS.txt`) and Windows file metadata, then installs it and reopens Vanta. The release must contain both the Setup EXE and a checksum source; Vanta never installs an unverified update.

After any desktop app change, refresh the Netlify screenshots and logo with `scripts\Refresh-Website-Images.ps1`. `scripts\Release.ps1` rejects a release when those committed previews are stale.

The app is **unsigned**. Public downloads may trigger Windows SmartScreen or browser reputation warnings. A trusted code-signing certificate and an established publisher identity are separate release steps; this project does not claim to provide them. Never disable security protections to distribute or run it.

## Compatibility and privacy

The app runs with the current user's permissions and does not request administrator access. Windows restricts synthetic input to targets at an equal or lower integrity level; protected targets can reject it. See Microsoft's [SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput). There is no anti-cheat bypass or guarantee that games will accept input. Use automation only where it is allowed.

Settings live in `%APPDATA%\Vanta Auto Clicker`. No network, telemetry, ads, service, startup entry, or account is used. Installed copies can be removed from Windows **Installed apps**; the uninstaller asks whether saved settings should also be deleted. Portable copies can be removed by deleting the executable. Cursor points are absolute physical desktop pixels, so recapture them after changing resolution or monitor layout. The application manifest enables DPI awareness; multi-monitor and mixed-DPI behavior should be checked on the intended machines.
