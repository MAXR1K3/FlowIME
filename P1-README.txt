FlowIME P1 stability-state-machine patch

Recommended install:
1. Close FlowIME.
2. Back up C:\FlowIME if desired.
3. Extract this project/patch into C:\FlowIME and allow overwrite.
4. In PowerShell at C:\FlowIME run:
     .\scripts\p0-check.ps1
5. Start FlowIME normally and run the P1 regression matrix in:
     docs\p1\P1-STABILITY-REPORT.md

Runtime diagnostic log:
  %LocalAppData%\FlowIME\logs\automation.log

Important: the patch-generation sandbox did not have .NET SDK 10.0.401, so the
Windows build/test gate must still be executed on the Windows development machine.
