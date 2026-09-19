FlowIME P8B.4.3 patch
Base: P8B.4.2 xUnit analyzer fix

Copy this patch over the P8B.4.2 project root and allow replacement.
Then run:
  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

Adds:
- Gameplay exit rule restore after the US-keyboard baseline.
- Non-activating, click-through input status overlay (中 / EN / US).
- Settings toggle and schema-v3 persistence for the overlay.
- Post-automation observation refresh so UI/overlay report applied state.
