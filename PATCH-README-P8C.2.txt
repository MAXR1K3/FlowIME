FlowIME P8C.2 patch
Base required: P8C.1 GameTextEntry Context Foundation.

Apply
1. Close FlowIME.
2. Extract this ZIP directly over the FlowIME project root and allow overwrite.
3. Run:
   dotnet test -c Debug
   dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

P8C.2 adds:
- per-game GameTextEntry profile persistence + Settings UI;
- high-confidence standard editable-control focus detection;
- non-blocking configured chat hotkey observation;
- per-game Keep / provider Chinese / provider English target;
- diagnostics and regression tests.

The hotkey monitor never consumes configured chat keys. No OCR, game-memory access,
process/DLL injection, typed-content capture, URL capture, or clipboard capture is used.
