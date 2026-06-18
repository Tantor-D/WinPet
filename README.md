# WinPet

WinPet 是一个 Windows 桌宠与久坐健康助手。它自动判断电脑活动情况，跟踪连续
工作和有效休息，在需要离开电脑时提醒你，并用完全本地的数据生成日、周趋势。

## 功能

- Windows 键鼠空闲、锁屏、休眠和唤醒检测
- 可配置最长工作时间、提前警告、有效休息和活动判定窗口
- 自动重置、手动重置、暂停和延后 5/10/15 分钟
- Windows 系统 Toast、应用内通知和桌宠气泡
- 系统托盘、开机启动、启动时隐藏和单实例运行
- SQLite 分钟桶、工作轮次、休息和提醒记录
- 今日概览、24 小时分布、最近 14 天趋势和高峰时段
- CSV ZIP 导出与带确认的历史数据清除
- Windows x64 与 ARM64 自包含发布包

## 完全兼容 Codex Pets

WinPet 直接读取 Codex 的自定义宠物目录：

```text
${CODEX_HOME:-$HOME/.codex}/pets/<pet-name>/
├── pet.json
└── spritesheet.webp
```

图集、九行动画、帧数和逐帧时长遵循 Codex Pets 契约，不需要转换或复制社区资源。
详见 [Codex Pets 兼容说明](docs/codex-pets-compatibility.md)。

## 隐私

WinPet 只读取“距上次键鼠输入经过多久”等系统状态，不记录按键内容、鼠标坐标、
窗口标题、应用名称、文件内容或浏览记录。数据默认只保存在：

```text
%LOCALAPPDATA%\WinPet
```

## 项目结构

- `src/WinPet.Core`：状态机、模型和跨平台接口
- `src/WinPet.Infrastructure`：SQLite、JSON 设置、Codex Pets 加载和导出
- `src/WinPet.Platform.Windows`：Windows 活动、通知和开机启动
- `src/WinPet.Desktop`：Avalonia 桌宠、托盘、设置和统计界面
- `tests`：核心、数据层和 Windows 集成测试

## 开发

需要 .NET 8 SDK 和 Windows 10 19041 或更高版本的 SDK。

```powershell
dotnet restore WinPet.sln --configfile NuGet.Config
dotnet build WinPet.sln --no-restore
dotnet test WinPet.sln --no-restore
./scripts/check-vulnerabilities.ps1
dotnet run --project src/WinPet.Desktop
```

## 发布

```powershell
./scripts/publish-windows.ps1 -RuntimeIdentifier win-x64 -Version 0.1.0
```

产物会写入 `artifacts/`。GitHub Actions 也支持手动生成 x64 和 ARM64 包。

## macOS

业务规则、数据格式、Codex Pets 契约和大部分 Avalonia UI 都不依赖 Win32。
Windows 平台能力已通过接口隔离，为后续 macOS 活动检测、菜单栏通知和登录项适配
保留了边界。当前发布版本仅支持 Windows。

## License

MIT
