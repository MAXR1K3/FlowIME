FlowIME P7 UI/UX patch

基线版本：FlowIME-P6-fixed2-source

使用方式：
1. 关闭正在运行的 FlowIME。
2. 将 patch 压缩包内容覆盖到 P6-fixed2 项目根目录。
3. 在项目根目录执行：
   dotnet test .\tests\FlowIME.App.Tests\FlowIME.App.Tests.csproj -c Debug
   dotnet build .\FlowIME.sln -c Debug
4. 启动：
   dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug

本 patch 只修改 UI/UX、规则页反馈、用于 UI 展示的派生属性以及 UI contract tests。
输入法状态机、Provider 实现、规则持久化算法、前台监听、托盘生命周期未改动。
