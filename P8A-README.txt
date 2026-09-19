FlowIME P8A - Context Awareness Foundation
===========================================

Baseline: P7.2.2 alignment fix.

P8A inserts the architecture required for future fullscreen/game/browser/tray
features while intentionally preserving current switching behavior.

New foundation:
- stable ApplicationIdentity (AUMID > PFN > unpackaged path hash > WindowsApps exe > process)
- InputContextEngine + isolated detector boundary
- InputDecisionEngine + deterministic context-policy priority
- runtime ManualOverrideGuard (20s default, top-level HWND + PID scoped)
- bounded privacy-safe AutomationDecisionJournal
- ForegroundContextService uses the same context/decision pipeline as automation
- packaged application rule authoring stores AUMID/PFN alongside the display path
- native AUMID discovery added to WindowResolver

No fullscreen/browser/game policy is active in P8A.
No provider mutation code is changed.
A backward-compatible optional AUMID match field is added; no schema migration is required.

Windows gate:
  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

Detailed design and acceptance matrix:
  docs\p8\P8A-CONTEXT-FOUNDATION-REPORT.md
