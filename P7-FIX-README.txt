FlowIME P7 fixed

修复：MainWindow.xaml.cs 中 ApplyInitialWindowBounds 的 WindowId 类型无法解析（CS0246）。
处理：将参数类型显式限定为 Microsoft.UI.WindowId。

核心状态机、Provider、规则持久化、前台监听与托盘逻辑未修改。

建议执行：
  dotnet test .\tests\FlowIME.App.Tests\FlowIME.App.Tests.csproj -c Debug
  dotnet build .\FlowIME.sln -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug
