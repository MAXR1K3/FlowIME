FlowIME P2 — resident lifecycle patch
=====================================

P1 automation state machine is unchanged.

P2 adds:
- single instance
- tray icon
- close-to-tray
- explicit tray Exit
- startup toggle via HKCU Run
- --background startup
- Explorer/taskbar restart recovery

After copying this patch over C:\FlowIME, run:

  .\scripts\p0-check.ps1

Then perform the manual lifecycle matrix in:

  docs\p2\P2-LIFECYCLE-REPORT.md

Important:
- X now hides FlowIME instead of exiting when the tray is available.
- To fully exit, right-click the tray icon and choose “退出”.
- Startup registration launches FlowIME with --background.
