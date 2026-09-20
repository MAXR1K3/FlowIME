# FlowIME Windows 安装器

使用 Inno Setup 7.x 为 FlowIME 生成 per-user、x64、自包含安装包。

## 构建

```powershell
.\installer\Build-Installer.ps1
```

默认流程：

1. 运行 Release 测试；
2. `dotnet publish` 生成 `win-x64` self-contained、多文件发布目录；
3. 验证 WinUI 的 PRI/XBF 资源存在；
4. 用 Inno Setup 7 编译安装器；
5. 输出到 `artifacts\installer\FlowIME-Setup-<version>-x64.exe`。

快速重建可使用：

```powershell
.\installer\Build-Installer.ps1 -SkipTests
```

版本号从 `Directory.Build.props` 经 MSBuild 自动读取；安装脚本不维护第二份版本号。

## 安装行为

- 默认目录：`%LOCALAPPDATA%\Programs\FlowIME`
- 用户数据：`%LOCALAPPDATA%\FlowIME`（安装、升级、卸载均默认保留）
- 无管理员权限、无 UAC
- 无桌面快捷方式，仅创建开始菜单入口
- 通过当前用户 `Run` 注册表项实现登录后后台启动
- 升级/卸载先通过单实例 IPC 请求退出，再按安装目录中的可执行文件绝对路径等待或终止；不会按进程名误杀其他目录的同名进程

## 发布取舍

FlowIME 使用 unpackaged WinUI 3、PRI/XBF 和原生依赖，因此保留稳定的 self-contained 多文件发布，不启用 single-file。正式公开发布前应使用项目的代码签名证书对安装包及主程序签名；本地构建不会伪造签名状态。
