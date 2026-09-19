# FlowIME P7 — UI/UX refinement

P7 是纯表现层与交互层升级。核心输入法状态机、规则解析、Provider、持久化与托盘生命周期均沿用 P6-fixed2。

## 目标

- 把开发期控制面板整理成可长期使用的 Windows 11 桌面工具。
- 明确“应用专属规则 > 全局默认 > 保持现状”的规则层级。
- 让当前应用、规则来源、目标输入状态和当前输入状态在首页直接可见。
- 对保存、启停、删除和失败操作提供明确反馈。
- 保持 PerMonitorV2，并让窗口初始尺寸按当前 DPI 换算。
- 不增加第三方 UI 框架，不修改稳定的输入法切换核心链路。

## 主要变化

### Shell

- NavigationView 改为 Auto，自适应 Expanded / Compact / Minimal。
- 默认窗口以 1120 × 760 有效像素为目标，并通过 `GetDpiForWindow` 换算为 AppWindow 所需的物理像素。
- 保留 Mica 与自定义 TitleBar。
- 全局统一页面标题、副标题、卡片、次级文字等样式。

### 首页

- 自动切换成为独立的主状态卡。
- 当前应用、规则来源、当前输入状态组成实时状态区。
- 宽窗口三列显示，窄窗口自动堆叠。
- “规则解析”聚焦本次目标，并保留“设置当前应用”快速操作。

### 规则

- 全局默认场景独立成高层级卡片，并明确显示启用状态与优先级语义。
- 应用规则重新排版：应用身份、路径、目标输入法/状态、启停、编辑与删除层次更清楚。
- 新增规则数量与启用数量摘要。
- 新增搜索无结果空状态。
- 新增操作成功 InfoBar；持久化/刷新异常会显示错误 InfoBar，而不是让用户只能猜测结果。

### 规则编辑

- 添加应用：上半区选应用，下半区集中设置“进入应用时”的输入法与状态。
- 编辑规则：应用身份与规则配置分区。
- 全局默认：直接展示匹配优先级，并解释关闭后的行为。

### 输入法支持

- Provider 从开发术语列表调整为支持状态卡。
- 保留 ConversionMode / OpenStatus 技术细节，但降为次级信息。
- 为后续第三方输入法 Provider 扩展保留清晰入口语义。

### 设置 / 关于

- 设置按“常规 / 诊断”分组。
- 暂未实现的输入状态提示明确标记为后续版本。
- 关于页统一产品信息与隐私说明。

## 核心逻辑保护

P7 未修改：

- `FlowIME.Core`
- `FlowIME.Windows`
- `FlowIME.Infrastructure`
- `FlowIME.Probe`
- `FlowIME.App/Services` 中的状态机、Provider、前台监听、托盘与恢复逻辑

仅 `RulesViewModel` 增加 UI 展示所需的计数/状态派生属性；没有改变规则匹配或持久化算法。

## 验证

当前执行环境没有 .NET SDK，因此无法在这里实际调用 `dotnet build/test`。已完成：

- 所有修改 XAML 的 XML 结构解析检查。
- 现有 UI contract 断言对应项静态复核。
- 新增 P7 UI contract tests，覆盖自适应导航、DPI 初始化、规则反馈、搜索空状态和统一样式。
- 对比 P6-fixed2，确认核心项目目录与 App Services 未发生修改。

Windows 实机建议执行：

```powershell
dotnet test .\tests\FlowIME.App.Tests\FlowIME.App.Tests.csproj -c Debug
dotnet build .\FlowIME.sln -c Debug
.\src\FlowIME.App\bin\Debug\net10.0-windows10.0.22621.0\win-x64\FlowIME.App.exe
```

重点实测：100% / 125% / 150% DPI、规则增删改启停、全局默认开关、搜索无结果、窗口宽度缩小时的导航与首页状态卡重排。
