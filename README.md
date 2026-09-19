# FlowIME

FlowIME 是一个面向 Windows 11 的输入法上下文自动切换工具。它监听前台窗口与输入焦点变化，根据应用规则自动切换输入法及中文/英文输入状态，减少在编辑器、终端、聊天软件和游戏之间手动切换输入法的成本。

> 当前项目处于开发阶段。生产自动化主要围绕 Microsoft Pinyin；微信输入法相关能力仍以发现、诊断和兼容性验证为主。

## 功能

- 按应用或进程创建输入状态规则
- 进入应用时选择“保持”“中文”或“英文”
- 支持进程路径、进程名、窗口类和窗口标题条件
- 支持规则优先级、冲突提示和匹配预览
- 前台窗口与输入焦点事件驱动，不持续轮询强制状态
- 系统托盘、单实例、关闭到托盘和开机启动
- 输入状态浮层
- 游戏场景识别、US 键盘基线和输入法快捷键保护
- 配置恢复、边界日志和诊断信息
- 原生 TSF/IMM/Win32 输入法诊断工具

## 安全与隐私原则

- 不注入目标进程
- 不记录键盘输入内容
- 默认不依赖模拟快捷键切换输入法
- 正常运行不需要管理员权限
- 应用配置与日志保存在当前用户的 `%LOCALAPPDATA%\FlowIME` 下

## 系统要求

### 运行

- Windows 11 x64
- Microsoft Pinyin（当前主要生产 Provider）

发布目录采用自包含方式生成，正常情况下无需额外安装 .NET Runtime。

### 开发与构建

- Windows 11 x64
- .NET 10 SDK
- Windows SDK 10.0.22621.0 或更高版本
- Visual Studio / Build Tools（包含 Windows 应用开发组件）

## 从源码构建

```powershell
git clone https://github.com/MAXR1K3/FlowIME.git
cd FlowIME

dotnet restore
dotnet build FlowIME.sln -c Release
dotnet test FlowIME.sln -c Release
```

## 发布 Windows 应用

生成自包含的 Windows x64 发布目录：

```powershell
dotnet publish src/FlowIME.App/FlowIME.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish/win-x64
```

启动：

```powershell
.\artifacts\publish\win-x64\FlowIME.App.exe
```

FlowIME 是未打包的 WinUI 3 桌面应用。请保留发布目录中的 DLL、资源和运行时文件，不要只复制单个 EXE。

## 使用方法

1. 启动 `FlowIME.App.exe`。
2. 在“规则”页面选择“添加应用规则”。
3. 在左侧选择正在运行的应用，或浏览一个尚未运行的 EXE。
4. 在右侧设置目标输入法及进入应用时的输入状态。
5. 根据需要展开“高级匹配”，设置进程名、窗口类、标题条件和优先级。
6. 保存规则并启用自动化。
7. 如需后台常驻，可在设置中启用关闭到托盘和开机启动。

## 项目结构

```text
src/
  FlowIME.App/             WinUI 3 桌面界面与应用生命周期
  FlowIME.Core/            规则、状态模型与核心抽象
  FlowIME.Infrastructure/  JSON 配置、日志与持久化
  FlowIME.Windows/         Windows TSF/IMM、窗口与游戏上下文实现
  FlowIME.Probe/           原生输入法诊断命令行工具
tests/
  FlowIME.*.Tests/         分层单元测试与 UI 契约测试
scripts/                   Windows 诊断与验收脚本
docs/                      设计、阶段报告和验证记录
```

## P0 输入法后端诊断

```powershell
.\scripts\p0-check.ps1

dotnet run --project src/FlowIME.Probe -- watch
dotnet run --project src/FlowIME.Probe -- inspect --hwnd 0x123456
dotnet run --project src/FlowIME.Probe -- set --hwnd 0x123456 --mode chinese
dotnet run --project src/FlowIME.Probe -- set --hwnd 0x123456 --mode english
```

测试结果记录在 `docs/p0/P0-INPUT-BACKEND-REPORT.md`。原生输入法后端只有在真实 Windows 桌面会话中完成验证后，才应视为通过。

## 微信输入法发现探针

```powershell
.\scripts\p5b-wechat-probe.ps1
```

该脚本只读取 TSF/IMM 状态并将采样写入 `%LOCALAPPDATA%\FlowIME\probe`，不会把 Microsoft Pinyin 专用的转换常量写入微信输入法。

## 配置和故障排查

运行数据默认位于：

```text
%LOCALAPPDATA%\FlowIME
```

遇到规则不生效时，建议依次检查：

1. FlowIME 自动化是否启用或处于暂停状态；
2. 目标规则是否启用，路径和进程名是否匹配；
3. 是否存在优先级更高的宽泛规则；
4. 当前输入法 Provider 是否受支持；
5. 设置页中的诊断状态和日志。

## 开发约束

- 保持前台/焦点事件驱动，避免固定频率轮询。
- 优先使用 Windows 原生 TSF/IMM API。
- Provider 特定逻辑必须位于 Provider 边界内。
- 修改规则模型、持久化格式或窗口检测逻辑时应同步补充测试。
- UI 修改应覆盖窄窗口、文本缩放、键盘操作和可访问性。

## 文档

详细设计与阶段验收记录位于 `docs/`：

- `docs/superpowers/specs/2026-09-18-flowime-v1-design.md`
- `docs/p0/P0-INPUT-BACKEND-REPORT.md`
- `docs/p3/P3-RULES-REPORT.md`
- `docs/p7/P7.2-RESPONSIVE-UI-REPORT.md`
- `docs/p8/`

## 许可证

当前仓库尚未声明开源许可证。公开可见不等于授予复制、修改或分发权；如需开放协作，请由项目所有者补充合适的许可证文件。
