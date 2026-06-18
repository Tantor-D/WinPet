# WinPet

WinPet 是一个以久坐提醒和电脑活动统计为核心的跨平台桌宠。

当前目标是先提供 Windows 版本，同时让核心逻辑、数据模型和 Avalonia UI
可以在未来复用于 macOS。

## 项目结构

- `src/WinPet.Core`：计时状态机、领域模型、统计口径和平台接口。
- `src/WinPet.Infrastructure`：SQLite、配置、主题加载和数据导出。
- `src/WinPet.Platform.Windows`：Windows 活动、锁屏和休眠检测。
- `src/WinPet.Desktop`：Avalonia 桌宠、托盘、设置和统计界面。
- `tests/WinPet.Core.Tests`：核心业务规则测试。
- `docs/implementation-plan.md`：详细实施计划。

## 隐私原则

WinPet 只读取“距上次键鼠输入经过多久”等系统级状态，不记录按键内容、
鼠标坐标、窗口标题或文件内容。统计数据默认仅保存在本机。

## 当前进度

- 已完成跨平台分层和核心工作/休息状态机。
- 已完成 Windows 键鼠空闲、锁屏和休眠检测。
- 已完成活动监测后台循环和实时诊断界面。
- SQLite、桌宠主题、系统托盘和统计视图仍在开发中。

## 本地运行

需要 .NET 8 SDK：

```powershell
dotnet restore WinPet.sln --configfile NuGet.Config
dotnet run --project src/WinPet.Desktop
```

运行测试：

```powershell
dotnet test WinPet.sln
```
