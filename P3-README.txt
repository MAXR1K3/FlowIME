FlowIME P3 rules/productization patch

Base: P2 fixed build that passed p0-check and real lifecycle acceptance.

P3 adds:
- current-app duplicate-safe rule creation/update
- richer current matched-rule status
- focus-driven status refresh
- rule enable/edit/delete
- pause/resume without unregistering WinEvent hooks
- immediate current-app reapply on resume
- tray pause/resume command

No rules JSON schema migration is required.

After overlaying this patch on C:\FlowIME run:
  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
  .\scripts\p0-check.ps1

Then launch:
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj

Real-machine checklist: docs\p3\P3-RULES-REPORT.md
