FlowIME P5C overlay patch

Base: P5B-3 fixed (real-machine multi-provider switching passed)
Target: P5C candidate — Global Default / Fallback

Install:
1. Exit FlowIME from the tray.
2. Extract this archive into C:\FlowIME and overwrite matching files.
3. In PowerShell from C:\FlowIME:
     Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
     .\scripts\p0-check.ps1
4. If the gate is green, start:
     dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj

Primary acceptance setup:
  Global default -> WeChat Input Method -> Chinese
  Notepad        -> Microsoft Pinyin     -> English

Important semantics:
- application-specific rule > global default > no action
- explicit Keep suppresses global default
- disabled app rule allows global default
- explicit app-rule execution failure never falls back to global default
- FlowIME's own process is excluded from fallback execution
- P5B-3 Microsoft/WeChat provider native algorithms are unchanged

See docs\p5\P5C-GLOBAL-DEFAULT-REPORT.md.
