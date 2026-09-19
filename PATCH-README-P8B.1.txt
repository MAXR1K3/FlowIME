FlowIME P8B.1 fullscreen-observation patch
===========================================

Base required: P8A.1 compile-fix (accepted/green).

Overlay this archive onto the repository root, preserving paths.
Then run:

  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

P8B.1 changes context observation only. It does not add a gameplay input policy and
therefore must not change existing application/global-default switching behavior.

See docs\p8\P8B.1-FULLSCREEN-OBSERVATION-REPORT.md for validation steps.
