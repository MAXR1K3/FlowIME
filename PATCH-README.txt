FlowIME P0 Focus-aware IMM probe patch

Overlay this directory onto C:\FlowIME and replace files when prompted.

Then run:
  .\scripts\p0-check.ps1

Read-only P0 sampling command:
  dotnet run --project .\src\FlowIME.Probe -- inspect --foreground --delay-ms 3000

After pressing Enter, switch to Notepad before the 3-second delay expires and
leave the text editor focused. The probe will resolve hwndFocus via
GetGUIThreadInfo and query IMM against that effective input HWND.
