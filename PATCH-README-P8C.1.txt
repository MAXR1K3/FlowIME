FlowIME P8C.1 patch

Base required: P8B.4.3 Gameplay Exit Restore + Input Status Overlay.

Copy the contents of this archive over the FlowIME repository root.

This patch adds only the GameTextEntry context foundation:
- runtime state + passive detector;
- child-context invariant (GameTextEntry requires Game);
- in-memory per-game profile contract/registry;
- higher-priority GameTextEntry decision policy;
- adapter-facing enter/exit seams;
- diagnostics and tests.

It does NOT automatically detect chat/text boxes, persist game profiles, or change the
validated Gameplay US/hotkey/overlay behavior by default.

After overwrite:
  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug
