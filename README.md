# FlowIME

[![License: GPL v3](https://img.shields.io/badge/License-GPL%20v3-blue.svg)](LICENSE)

FlowIME 是一款 Windows 11 输入法自动切换工具。

它会记住不同应用需要的输入状态：进入终端或编辑器时切到英文，回到聊天软件时恢复中文，打开游戏时使用 US 键盘布局。切换由窗口和输入焦点变化触发，不会持续轮询或反复抢占输入法状态。

> 当前主要适配 Microsoft Pinyin。微信输入法相关能力仍在验证和完善。

## 功能

- 按应用设置“保持”“中文”或“英文”
- 支持进程路径、进程名、窗口类、窗口标题和规则优先级
- 切换时显示“中”“EN”或“US”浮层
- 浮层可固定在屏幕边缘，也可跟随输入光标
- 显示当前输入法在 Windows 中注册的品牌图标，并适配深色、浅色主题
- 提供游戏 US 键盘基线、文本输入场景识别和快捷键保护
- 支持系统托盘、关闭到托盘、开机启动和单实例运行
- 保存规则、设置、日志及诊断信息


## 下载

从 [Releases](https://github.com/MAXR1K3/FlowIME/releases/latest) 下载最新的 `FlowIME-*-win-x64.zip`。

解压完整目录后运行 `FlowIME.App.exe`。发布包为自包含版本，通常不需要另外安装 .NET Runtime。请保留目录中的 DLL 和资源文件，不要只复制 EXE。

### 系统要求

- Windows 11 x64
- Microsoft Pinyin

## 使用

1. 启动 FlowIME，进入“规则”页面。
2. 点击“添加应用规则”，选择正在运行的应用或浏览 EXE。
3. 设置目标输入法和进入应用时的输入状态。
4. 保存规则并启用自动化。

一般只需先配置几个常用应用，例如终端设为英文、聊天软件设为中文。需要区分同一应用里的不同窗口时，再添加标题、窗口类或优先级条件。

FlowIME 可以常驻托盘。暂时不需要自动切换时，直接暂停自动化即可，不必删除规则。

## 游戏

FlowIME 可以在非输入状态下维持 US 键盘布局，减少输入法与游戏快捷键冲突；进入文本输入场景后，再按配置恢复输入状态。

不同游戏提供的窗口和光标信息不同。完全自绘的聊天框可能无法可靠跟随光标，这种情况下可以改用固定位置或关闭浮层。

## 隐私

- 不记录键盘输入内容
- 不向目标进程注入代码
- 默认不依赖模拟快捷键切换输入法
- 正常运行不需要管理员权限
- 配置和日志保存在 `%LOCALAPPDATA%\FlowIME`

日志记录规则匹配、输入法操作结果和错误信息，不记录聊天内容、密码或实际键入的文字。

## 排查

规则没有生效时，检查以下项目：

1. 自动化是否启用。
2. 应用路径和进程名是否匹配。
3. 是否有优先级更高的规则覆盖当前规则。
4. 当前输入法 Provider 是否受支持。
5. 设置页中的诊断状态及 `%LOCALAPPDATA%\FlowIME\logs` 下的日志。

更新版本后，请先从托盘完全退出旧实例。FlowIME 采用单实例运行，旧版本仍在后台时，新版本不会启动第二套自动化服务。

## 从源码构建

开发环境：

- Windows 11 x64
- .NET 10 SDK
- Windows SDK 10.0.22621.0 或更高版本
- Visual Studio 或 Build Tools，包含 Windows 应用开发组件

```powershell
git clone https://github.com/MAXR1K3/FlowIME.git
cd FlowIME

dotnet restore
dotnet build FlowIME.sln -c Release
dotnet test FlowIME.sln -c Release
```

生成 Windows x64 自包含发布目录：

```powershell
dotnet publish src/FlowIME.App/FlowIME.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish/win-x64
```

## 项目结构

```text
src/
  FlowIME.App/             WinUI 3 界面与应用生命周期
  FlowIME.Core/            规则、状态模型与核心抽象
  FlowIME.Infrastructure/  JSON 配置、日志与持久化
  FlowIME.Windows/         Windows TSF/IMM、窗口与游戏上下文实现
  FlowIME.Probe/           输入法诊断命令行工具
tests/                     分层测试与 UI 契约测试
scripts/                   Windows 诊断与验收脚本
docs/                      设计和验证记录
```

输入法后端和兼容性诊断命令见 [`docs/`](docs/) 与 [`scripts/`](scripts/)。

## 许可证

FlowIME 自有源代码采用 [GNU General Public License v3.0 only](LICENSE)。如果分发修改版或基于本项目形成的衍生作品，需要继续采用 GPL-3.0，并按许可证要求提供对应源代码。商业使用和收费分发仍然允许，但不能将 GPL 覆盖的衍生作品改为闭源发布。

第三方依赖和随发布包分发的组件仍遵循各自的许可证。
