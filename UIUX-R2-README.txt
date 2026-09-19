FlowIME UI/UX R2
================

Base: P8C.2 GameTextEntry Detection

This branch freezes P8C feature expansion and upgrades the user interface only.

Highlights:
- Primary navigation: 首页 / 规则 / 游戏
- 输入法支持 removed from primary navigation
- 关于 moved to footer
- Game and general settings presented as separate modes on the same settings runtime
- Home redesigned around current app + current context + input state
- Game hotkey details moved into a collapsible advanced section
- Dedicated responsive GameTextEntry configuration dialog
- Refined warm-beige light surfaces; Windows-native dark theme remains

Validation on a Windows development machine:

  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

Feature logic in Core/Windows/Infrastructure/Probe/App Services is unchanged from P8C.2.
