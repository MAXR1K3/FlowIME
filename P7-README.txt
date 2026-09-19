FlowIME P7 — UI/UX refinement

基线：P6-fixed2
范围：UI/UX、反馈、窗口 DPI 适配；不修改输入法状态机。

Windows 测试：
  dotnet test .\tests\FlowIME.App.Tests\FlowIME.App.Tests.csproj -c Debug
  dotnet build .\FlowIME.sln -c Debug
  .\src\FlowIME.App\bin\Debug\net10.0-windows10.0.22621.0\win-x64\FlowIME.App.exe

详细改动：docs\p7\P7-UIUX-REPORT.md
