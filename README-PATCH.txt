FlowIME P0 Conversion Mode Probe patch

1. Close any running FlowIME.Probe process.
2. Extract this ZIP directly into C:\FlowIME and allow overwrite.
3. From PowerShell in C:\FlowIME run:
   .\scripts\p0-check.ps1
4. Then inspect the same Notepad HWND:
   dotnet run --project .\src\FlowIME.Probe -- inspect --hwnd 0x211488
5. Test conversion mode:
   dotnet run --project .\src\FlowIME.Probe -- set-conversion --hwnd 0x211488 --mode chinese
   dotnet run --project .\src\FlowIME.Probe -- set-conversion --hwnd 0x211488 --mode english

The legacy OpenStatus path is retained only as evidence; do not treat it as actual Chinese/English state.
