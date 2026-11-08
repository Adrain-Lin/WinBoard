# WinBoard

Windows 桌面项目工作台、应用 Dock 和独立桌面组件。使用 C#、WPF 和 Windows 系统接口实现。

开发者：**AdrainLin** · 开发时间标注：2024年11月4日 · 当前版本：**1.2.0**

## 下载与安装

前往 [Releases](https://github.com/Adrain-Lin/WinBoard/releases/latest)：

- `WinBoard-Setup-1.2.0.exe`：双击一键安装，为当前用户创建桌面和开始菜单快捷方式；可选择开机自启动。
- `WinBoard-1.2.0-win-x64.zip`：解压到固定、可写目录，运行 `WinBoard.exe`。

支持 Windows 10/11 **x64**，需要 .NET Framework 4.x（建议 4.8）。安装包未进行数字签名；本项目不支持 macOS、Linux 或 Windows ARM 原生运行。

## 功能

- 项目分组：文件、文件夹、快捷方式和网址入口，搜索、拖动排序、自定义项目颜色。
- 紧凑栏：固定应用、运行窗口展示、拖入入口和排序，系统入口与应用合并显示，提供两种显示样式。
- 紧凑栏支持位置锁定、最大化窗口时自动隐藏；打开工作台时仍可保留紧凑栏。
- 时钟（数字/圆盘）、天气、备忘录、悬浮控制中心，可移动、调整大小、固定位置。
- 各窗口独立设置材质、透明度、颜色，组件支持字体颜色、隐藏边框与顶部控件。
- 音量、亮度、电池/充电状态、网络与蓝牙状态及 Windows 设置入口。
- 文件拖入紧凑栏回收站，删除到系统回收站；带回收动画。
- 关机、重启和待机；关机/重启确认后先保存并退出 WinBoard，再执行系统操作。

详见 [使用说明](使用说明.md)。

## 构建

在 Windows PowerShell 中运行（无需 NuGet）：

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
powershell -ExecutionPolicy Bypass -File .\Build-Installer.ps1
powershell -ExecutionPolicy Bypass -File .\Package.ps1 -Executable WinBoard.exe
```

`Build.ps1` 使用系统 `.NET Framework64/v4.0.30319/csc.exe` 与 WPF 程序集。构建需要对应的 .NET Framework 组件。`Build-Installer.ps1` 生成 `dist/WinBoard-Setup-1.2.0.exe`，`Package.ps1` 生成便携 ZIP。

源码按 `WinBoard.*.cs` 拆分，构建文件清单见 `Build.ps1`；`installer/Setup.cs` 为安装/卸载程序，`assets/` 包含图标与生成说明。

## 数据与升级

配置、布局和备忘录保存在程序目录 `data/`。升级前退出应用并备份该目录；替换程序时保留 `data/`。安装版卸载会保留个人数据。仓库和发布包不包含开发者的个人配置、工程或桌面文件。

关闭或最小化工作台会保留紧凑栏；真正退出请使用托盘菜单。移动便携版目录后请重新设置开机自启动。

## 已知限制与外部服务

- 玻璃为 Windows 模糊/透明效果的近似实现；具体效果取决于系统版本、合成器和远程桌面环境。
- 网络/蓝牙操作通过 Windows 设置完成，并非完整替代系统配对和网络管理；蓝牙设备状态可能无法覆盖所有低功耗设备。
- 屏幕亮度调节依赖硬件支持，部分外接显示器不可直接调节。
- 天气和城市搜索请求 [Open-Meteo](https://open-meteo.com/)，使用其服务须遵守对应条款。天气数据服务的授权不由本项目 MIT 协议代替。
- Windows 管理的任务栏、应用窗口识别和图标提取可能受系统或应用升级影响。图示的第三方应用图标属于各自权利人，不作为本项目资源分发。

## 协议

本项目采用 **MIT License**，允许使用、修改、分发和商业使用，须保留版权及许可声明；软件按原样提供，不提供担保。完整条款见 [LICENSE](LICENSE)。肉夹馍图标由 AI 生成，制作说明见 [assets/图标设计说明.md](assets/图标设计说明.md)。
