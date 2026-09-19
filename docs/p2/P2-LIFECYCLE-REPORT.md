# FlowIME P2 Lifecycle / Resident-App Report

## Status

**SOURCE IMPLEMENTED — Windows build and real-machine lifecycle validation still required.**

P1 core automation is intentionally left unchanged in this phase. P2 only wraps the
existing automation process with production-style Windows lifecycle behavior.

The current execution sandbox does not contain the .NET 10 SDK, so `dotnet build`
and `dotnet test` cannot be executed here. Source/XAML structural checks were run,
but this report must not be treated as a Windows runtime pass until the manual matrix
below is completed.

## P2 scope

Implemented:

- single-instance process coordination;
- second normal launch restores the primary window instead of starting a second
  automation engine;
- `--background` launch mode for silent startup;
- notification-area icon with Open and Exit actions;
- closing the main window hides it while automation remains alive;
- explicit Exit disposes the tray, single-instance listener, automation coordinator,
  foreground/focus WinEvent hooks, foreground context service and rule repository;
- Explorer/taskbar restart handling through the registered `TaskbarCreated` message;
- current-user startup registration through
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
- startup registration command is quoted and appends `--background`;
- a Run-key command longer than the documented 260-character limit is rejected;
- if tray initialization fails, a `--background` launch falls back to showing the
  main window rather than leaving an invisible process with no user exit path.

Not implemented in P2:

- installer/MSIX packaging;
- auto updater;
- custom tray icon asset/branding;
- configurable close-to-tray policy;
- input-state toast/overlay indicator;
- changes to the P1 retry/focus/IME backend.

## Single-instance contract

The first process owns a named Windows mutex and a named auto-reset event.

- Normal second launch: signal the primary event, then exit.
- Primary listener: enqueue `ShowMainWindow` onto the WinUI dispatcher.
- `--background` second launch: exit silently without showing the primary window.
- Explicit primary shutdown releases the mutex and stops the listener.

This prevents two FlowIME automation coordinators from competing over the same
foreground/focus stream.

## Tray and window contract

`TrayIconService` owns a hidden Win32 window on a dedicated STA thread and adds a
notification-area icon using `Shell_NotifyIconW`.

- left click: open/restore FlowIME;
- right click: native menu with `打开 FlowIME` and `退出`;
- `TaskbarCreated`: re-add the icon after Explorer/taskbar restart;
- normal main-window close: cancel `AppWindow.Closing` and call `AppWindow.Hide()`;
- Open: `AppWindow.Show(true)`;
- Exit: allow the window to close, then dispose all services.

Tray initialization is deliberately non-fatal to the input automation backend. If
tray setup fails, close-to-tray is disabled and the main window remains visible.

## Startup registration contract

The Settings page now controls a real unpackaged-app startup entry instead of the old
`StartupTask` placeholder.

Registry path:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

Value:

`FlowIME`

Command:

`"<current FlowIME.App.exe path>" --background`

The setting represents FlowIME's Run-key registration. Windows or enterprise policy
can still suppress startup independently of this registration.

## Added regression coverage

Source tests now cover:

- background argument parsing;
- startup command quoting and `--background` suffix;
- rejection of quoted executable paths;
- rejection of Run-key commands over 260 characters;
- named secondary-instance signaling to the primary listener;
- Settings XAML exposes the real startup control and no longer contains the obsolete
  StartupTask placeholder;
- tray source includes `Shell_NotifyIconW`, Explorer restart handling, Open and Exit.

## Build gate

From the repository root on the Windows development machine:

```powershell
.\scripts\p0-check.ps1
```

That remains the full restore/build/test gate for the solution.

## P2 manual acceptance matrix

After the build gate is green:

1. Launch FlowIME normally. Main window is visible and exactly one FlowIME tray icon
   exists.
2. Configure a known Codex/Notepad rule pair and confirm P1 switching still works.
3. Close the main window with X. The window disappears but rules keep switching.
4. Left-click the tray icon. The same window returns; no second FlowIME process is
   created.
5. Hide the window, then launch `FlowIME.App.exe` again. The existing window returns
   and Task Manager still shows only one FlowIME application process.
6. Right-click tray -> `退出`. The FlowIME process exits and automation stops.
7. Re-launch and enable `随 Windows 登录启动` in Settings. Confirm the registry value
   is the current executable path plus `--background`.
8. Launch `FlowIME.App.exe --background`. The tray icon appears but the main window
   does not flash/open.
9. While a primary instance is already running hidden, launch another process with
   `--background`; it exits without forcing the hidden primary window open.
10. Restart Windows Explorer from Task Manager. The FlowIME tray icon must return.
11. Reboot/sign out-in with startup enabled. FlowIME starts in the tray without
    opening the main window and the P1 app rules still apply.
12. Disable startup in Settings and verify the `FlowIME` Run value is removed.

If any lifecycle case fails, preserve `%LocalAppData%\FlowIME\logs\automation.log`
and note whether the problem is tray/window/startup only or whether P1 switching also
regressed.
