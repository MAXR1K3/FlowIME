FlowIME UI/UX R3.2 Home patch
Base required: FlowIME UI/UX R3.1 Foundation

Copy this archive over the project root and allow replacement of existing files.
Then run:
  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

Feature baseline remains P8C.2. Core, Windows, Infrastructure, Probe, and App Services are not changed.
