# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Fixed
- **Discord could stay un-muted after a Sonar restart.** If Windows/Discord kept restoring the un-muted state and the app gave up after 3 re-mutes, that was wrongly remembered as "the user un-muted it on purpose", so a later reconnect left the session alone. Now only an un-mute that happens after the 15-second protection window counts as a deliberate user choice.
- **Slow start after login.** Hiding Sonar Chat/Gaming at startup enumerated every audio device on the UI thread and could delay the app when the audio service was still starting. It now runs in the background. If the app is closed while this is still running, the devices are restored correctly and are not hidden again afterwards.
- **Audio-engine safety.** New Discord sessions are now handled on a worker thread instead of inside the Windows audio notification callback, like the other callbacks already were.
- **Checkbox, buttons and panels on scaled displays.** The checkbox box, tick, focus ring, corner radius and borders now scale with the display scaling (125%, 150%, ...) instead of staying at a fixed pixel size next to the larger text.
- **Warnings are no longer silently dropped.** Balloon notifications are limited to one per minute, but a warning that arrived inside that minute used to be lost. It is now queued and shown afterwards (duplicates are skipped).

### Changed
- **Closing the first-run window without saving now asks first.** The app tells you that Discord is not protected yet and lets you stay in the window, instead of quitting silently.

### Added
- **Keyboard focus indicator** on the custom buttons and checkboxes, so Tab navigation shows which control is selected.

## [1.0.30] - 2026-10-30

### Changed
- **Start with Windows is now pre-ticked on first run.** The checkbox in Settings is ticked by default until you press Save for the first time, so a fresh install protects you after a reboot. Nothing is registered with Windows until you confirm with Save, and closing the window without saving leaves no startup entry behind.
- **Update dependencies moved from preview to stable.** `Microsoft.Bcl.AsyncInterfaces`, `System.IO.Pipelines`, `System.Text.Encodings.Web` and `System.Text.Json` went from `11.0.0-rc.1` to `9.0.0`.
- **Smaller download.** Removed the unused `NAudio`, `NAudio.Asio`, `NAudio.Midi`, `NAudio.WinForms` and `NAudio.WinMM` packages. Only `NAudio.Core` and `NAudio.Wasapi` are needed. This also reduces antivirus false positives.

### Fixed
- **Exit no longer un-mutes sessions you muted yourself.** On exit the app now un-mutes only the Discord sessions it muted. A session you muted manually stays muted.
- **Your manual un-mute is no longer overridden after a device event.** When Sonar restarts or an audio device changes, the app re-attaches to Discord's sessions. A session you had already un-muted on purpose is now left alone instead of getting a new 15-second protection window. New sessions (for example a new call) are still muted as usual.
- **Burst of audio device events handled once.** Restarting Sonar makes Windows send dozens of device notifications. The app now waits one second after the last one and re-checks once, instead of re-binding for every event. This means less CPU use and a faster, cleaner reconnect.
- **Safer Windows API call for hiding Sonar Chat/Gaming.** The `IPolicyConfig` interface now declares its string and boolean parameters explicitly (`LPWStr`, `Bool`) instead of relying on the interop defaults, which only worked by accident. This makes the optional hide feature less likely to break after a Windows update.

### Not changed
- Update checking and publishing (`AppUpdater.cs`, `publish.ps1`) are unchanged.

### Notes for maintainers
- Not built or run on Windows when these changes were made. Before releasing, restore packages, build, and test: open a Discord call, un-mute Discord from the Windows mixer, restart Sonar, and confirm Discord is not muted again.
- Verify that `NAudio.Wasapi` does not need `NAudio.WinMM` at runtime, and that `AudioSessionControl.GetSessionInstanceIdentifier` exists in your NAudio version.
