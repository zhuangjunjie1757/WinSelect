# WinSelect

轻量的 Windows 多显示器切换工具。常驻系统托盘，通过快捷键呼出面板，一键开启或关闭指定显示器的输出。

适用于 Windows 10 / 11 x64，使用 C#、WPF 和 Windows DisplayConfig API，基于 .NET Framework 4.8，无第三方运行库依赖。

## 下载与运行

前往 [Releases](https://github.com/zhuangjunjie1757/WinSelect/releases) 下载 `WinSelect-v1.2.1-win-x64.zip`，解压到固定目录后，双击 `WinSelect.exe` 即可运行，无需安装或管理员权限。

便携包包含：

```text
WinSelect.exe          主程序
WinSelect.exe.config   .NET 运行时与混合 DPI 配置
README.md              使用说明
LICENSE                MIT 许可证
```

请始终将 `WinSelect.exe.config` 与 EXE 放在同一目录。如果单独下载 EXE，也请下载 Release 中的同名配置文件。`SHA256SUMS.txt` 提供下载文件的 SHA-256 校验值。

运行要求：

- Windows 10 / 11，64 位。
- .NET Framework 4.8 或兼容的更新版本。
- 本机交互式桌面，以及支持 Windows DisplayConfig 的显卡驱动。

## 功能

- **独立控制显示器**：支持同时开启多块屏幕，至少保留一块屏幕开启。
- **屏幕预览**：显示名称、截图、分辨率、刷新率和接口，按显示器布局排列。
- **全局快捷键**：默认 `Ctrl + Alt + D`，可在设置中修改。
- **托盘操作**：点击图标呼出面板，右键打开设置或退出。
- **记忆显示配置**：重新启用时优先恢复保存的位置、分辨率、刷新率和旋转方向。
- **混合 DPI 支持**：面板和设置窗口按所在屏幕的缩放比例显示。
- **可选开机启动**：登录后静默常驻托盘，默认关闭。
- **单实例运行**：再次打开 EXE 会呼出已运行的程序。

## 使用方法

首次启动会在鼠标所在屏幕的右下角显示面板，并在系统托盘放置双屏图标。图标可能收在任务栏的隐藏图标区域中。

| 操作 | 效果 |
| --- | --- |
| `Ctrl + Alt + D` | 在所有已启用屏幕底部居中显示面板；再次按下收起 |
| 单击托盘图标 | 在鼠标所在屏幕的托盘附近显示面板 |
| 点击屏幕下方的圆环 | 立即切换该屏幕的输出状态 |
| `Esc` 或点击其他窗口、桌面 | 收起面板，程序继续后台运行 |
| 右击托盘 → 设置 | 更改快捷键、开机启动 |
| 右击托盘 → 退出 | 退出程序并移除托盘图标 |

蓝色圆环和中心蓝点表示输出已开启，空心圆环表示输出已关闭。关闭的显示器仍保留在面板中，可以重新开启。切换立即执行，没有确认倒计时；退出程序不会自动恢复或改变显示器输出。

WinSelect 控制的是 Windows 的显示输出。停用后显示器通常会提示“无信号”并进入自身的待机流程；它不控制显示器的物理电源，也不切换 HDMI / DP 输入源。恢复配置受显卡和驱动支持情况影响，失败时会显示错误信息。

截图在每次呼出面板时获取一次，普通使用时仅保存在内存中，不持续刷新、不写入磁盘。

## 设置与数据

右击托盘图标进入设置，修改后即时保存：

- **快捷键**：点击按键框，再按包含 `Ctrl`、`Alt` 或 `Shift` 的组合键；`Esc` 取消录入。不支持 Win 键组合和 F12。若快捷键被占用，会提示错误并保留原配置。
- **开机启动**：打开后，登录 Windows 时仅启动到托盘，不主动弹出面板。

本地数据保存在：

```text
%LOCALAPPDATA%\WinSelect\settings.xml   快捷键与显示配置
%LOCALAPPDATA%\WinSelect\WinSelect.log  错误日志
```

开机启动写入当前用户的注册表项 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WinSelect`，指向当前 EXE 路径。移动程序后，请在新位置将开机启动关闭再开启，以更新路径。

更新时先退出托盘程序，再解压新版本并替换程序文件，本地设置会保留。卸载时先关闭开机启动、退出程序，再删除程序目录；需要清除设置时，可一并删除 `%LOCALAPPDATA%\WinSelect`。

## 从源码构建

在 Windows PowerShell 中进入项目目录执行。构建使用系统 .NET Framework 编译器和 WPF 程序集，无需安装 .NET SDK，也无需还原 NuGet 包。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本生成多尺寸图标，并在项目根目录生成 `WinSelect.exe`。运行时仍需同目录下的 `WinSelect.exe.config`。也可使用安装了 .NET Framework 4.8 开发组件的 Visual Studio 打开 `WinSelect.csproj`。

若旧版本正在运行，可先通过托盘退出，或执行：

```powershell
Start-Process .\WinSelect.exe -ArgumentList '--quit' -WindowStyle Hidden -Wait
```

确认进程退出后再构建，避免 EXE 被占用。

### 打包发布文件

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

打包脚本先重新构建，从 EXE 读取版本号，再生成便携 ZIP、独立 EXE、配置文件及 SHA-256 校验文件，输出到 `dist/v1.2.1/`。`dist/`、编译产物和开发检查报告均不纳入源码版本管理。

### 开发检查

先退出正在运行的 WinSelect，再执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\check.ps1
```

脚本重新编译并运行 `--verify` 和 `--ui-check`。报告输出到 `work/diagnostics/`，失败时以非零退出码退出。

| 参数 | 用途 |
| --- | --- |
| `--diagnose` | 读取显示设备、布局、接口、DPI 和原生结构体信息 |
| `--verify` | 验证显示配置、最后一块屏幕保护和离屏渲染；保存当前布局参考 |
| `--ui-check` | 短暂显示窗口，检查位置、DPI、快捷键消息处理、托盘菜单和设置窗口，随后退出 |
| `--background` | 仅启动到托盘，不弹出面板 |
| `--quit` | 通知已运行的实例正常退出 |

分别运行检查时，请使用 `Start-Process -Wait` 等待桌面程序完成，例如：

```powershell
Start-Process .\WinSelect.exe -ArgumentList '--diagnose' -WindowStyle Hidden -Wait
```

自动检查不会应用显示器输出更改，不能替代真实硬件上的断开与恢复测试。`--verify` 和 `--ui-check` 会生成可能包含桌面内容的 PNG，仅用于本地开发检查，请勿随发布包上传。

## 项目结构

```text
src/
  Program.cs          启动、单实例、托盘、快捷键、窗口协调和诊断
  DisplayService.cs   显示器枚举、配置验证和输出切换
  Native.cs           Windows 原生接口与结构体
  Views.cs            屏幕面板、截图和设置窗口
  TrayMenuWindow.cs   托盘右键菜单
  Storage.cs          配置持久化和错误日志
  AssemblyInfo.cs     程序版本信息
tools/IconBuilder.cs 多尺寸应用图标生成器
assets/              PNG 图标
build.ps1            编译脚本
check.ps1            配置与界面检查脚本
package.ps1          Release 打包脚本
WinSelect.csproj     Visual Studio 项目
WinSelect.exe.config .NET 运行时与 DPI 配置
app.manifest         权限与 DPI 声明
```

## 常见问题

**找不到托盘图标或面板？** 先查看任务栏隐藏图标区域，或再次运行 EXE 呼出已有实例。

**快捷键没有反应？** 可能被其他程序占用，右击托盘进入设置，更换组合键。

**预览画面没有更新？** 预览是本次呼出时的一帧截图，收起并重新呼出即可更新。

**无法关闭最后一块屏幕？** 程序会保留至少一块可操作的屏幕。

**编译提示文件被占用？** 先退出正在运行的 WinSelect，等待进程结束后再编译。

## 许可证

本项目采用 [MIT License](LICENSE)。
