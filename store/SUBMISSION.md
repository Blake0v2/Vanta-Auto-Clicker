# Vanta Store submission draft

This text is prepared for the owner to review and enter in Partner Center. It has not been submitted. A support contact, public privacy-policy URL, actual product identity, Store screenshots, age-rating questionnaire, and availability choices still need completing.

## Short description

Mouse automation with adjustable timing, global shortcuts, and cursor sequences.

## Description

Vanta Auto Clicker is a Windows desktop utility for repetitive mouse input. Choose a click rate or delay, select a mouse button, and start or stop with a configurable global shortcut.

The Default view keeps common controls together. The Advanced view adds click and time limits, double clicks, timing variation, and an ordered sequence of saved cursor positions. Both views share the same settings.

Try your configuration in the built-in Test pad. Vanta starts idle, includes an emergency stop shortcut, and stops clicking when Windows locks or suspends. Import and export profiles to move your settings between PCs.

No account, advertisements, or telemetry is included in the app. Microsoft Store manages updates for this edition. Windows scheduling and the destination app determine effective click speed. Elevated or protected applications may reject synthetic input. Use automation only where permitted.

## Features

- Configurable global shortcut with toggle and hold modes.
- Left, middle, and right mouse buttons.
- Rate or delay timing and adjustable press duration.
- Click/time limits, double clicks, and interval variation.
- Ordered cursor sequences with F6 position capture.
- Profile import/export and automatic local preference saving.
- Built-in click counter test pad.

## Suggested category

Utilities & tools; confirm the available Partner Center category and subcategory when submitting.

## runFullTrust justification

Vanta is a C#/.NET Framework WPF desktop application. It needs normal desktop process access to implement user-configured global keyboard shortcuts with SetWindowsHookEx, mouse clicks with SendInput, cursor-position capture, and Windows session/power notifications. It runs at the current user's ordinary privilege level, does not request administrator access or UIAccess, and cannot inject input into higher-integrity targets. It installs no driver or service. The keyboard hook handles shortcut state only and does not log or transmit keystrokes. The Store build contains no external updater; Microsoft Store manages installation and updates.

## Notes for certification

No login or test credentials are required. Open Vanta from Start. It starts idle. Open Test pad, move the pointer inside the outlined area, and press F8 to toggle clicking. The counter should increase. Press Esc to stop. Keep tests in the test pad to avoid interacting with other applications. Closing the test pad also stops the run.

In Advanced view, enable a click limit of 5 and repeat the test to observe bounded input. Settings allows profile import/export and viewing the bundled font license. The update button opens Microsoft Store's updates page. This edition must not start or download the GitHub Setup installer.

Initial package target: x64 Windows 10 build 19041 or later / Windows 11, .NET Framework 4.8+. Packaged-context testing and actual Store certification remain pending.

## Privacy-policy draft

Vanta Auto Clicker stores preferences, configured shortcuts, and saved cursor coordinates locally on your device. Profiles are written to or read from files you choose when using export/import. Its global keyboard hook identifies configured shortcuts and emergency-stop/capture keys; it does not maintain a keystroke log or transmit keystrokes.

The Microsoft Store edition includes no telemetry, advertising, analytics, account system, or app-operated network service. When you open Microsoft Store from Vanta, Store downloads, updates, and associated data processing are handled by Microsoft under Microsoft's privacy terms.

Uninstall Vanta through Windows Settings. Profile files you exported remain where you saved them and can be deleted there. Add an owner-controlled contact method and publish the reviewed policy at an HTTPS URL before entering it into Partner Center.
