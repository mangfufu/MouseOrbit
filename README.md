# MouseOrbit

Windows 鼠标快捷工具，用侧键面板、轮盘和右键手势切换窗口、截图及调节音量。

## 使用

- 轻点侧键：打开窗口选择。在鼠标旁显示名称列表，滚轮连续浏览；多窗口直接插入当前行下方，再点应用名收起。靠近边缘时适配屏幕工作区。
- 长按侧键：打开轮盘，移到目标并看到选中反馈后松开执行。按文字与图标区域选择；中心、空隙或远处松开取消。
- 按住右键：划手势，松开执行。左右分屏，上滑最大化／还原，下滑最小化，画圈切换桌面，C 复制，V 粘贴。
- 右滑后接着上下拖动：连续调节音量，无需停顿。
- 点“截图”：框选后可标注、复制、保存，也可选择 GIF 录制。GIF 录完先预览。
- 指向一行后，点右侧的小折角打开分类、截图和设置菜单；轮盘、专注模式和管理收在设置里。

左右半屏再次触发同一方向会还原之前的窗口尺寸、位置和最大化状态；左右互换时仍保留最初状态。对启动前就已半屏、没有历史记录的窗口，再次触发同方向会退出半屏并居中。

轮盘使用半透明材质，中心留空。已置顶的目标窗口显示“取消置顶”和状态圆点。GIF 录制默认显示鼠标，可关闭；结束后在录制区域附近打开预览，可暂停并用矩形、圆形、箭头、画笔、文字标注，支持颜色、粗细、字号、撤销与重做。标注应用于整段 GIF。

支持多显示器、低饱和半透明的字场样式、常用与最近项目移除，以及自动／手动专注模式。默认侧键 1（后退），可在设置中改为侧键 2（前进）或中键。关闭面板后驻留托盘，从托盘退出。

窗口列表直接显示原生图标和名称，呼出时根据附近背景选择深浅材质，逐项淡入。每格细粒子缓慢流动，指向、切换分类和展开窗口时平滑过渡，菜单从当前行接向侧面；图标、文字和点击位置保持稳定。关闭后停止动效，系统关闭动画时显示静态效果。展开同应用窗口或查看文件时保留短标题。列表打开期间，滚轮不会传给后面的应用；右键或再次轻点侧键关闭。

## 运行

需要 Windows 10/11（x64 或 ARM64）。克隆后双击 `启动 MouseOrbit.cmd` 即可：

- 自动检查 .NET 10 SDK 和 Windows 桌面运行时；已有兼容环境时直接复用。
- 缺少环境时，使用[微软官方安装脚本](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script)下载 .NET 10，安装到 `%LOCALAPPDATA%\MouseOrbit\dotnet\<架构>`，无需管理员权限，也无需重启。
- 自动还原依赖、编译当前源码并打开程序。首次配置需要联网，下载可能需要几分钟；后续启动复用已安装的环境。
- 不更改系统 PATH，不影响已有的 .NET 版本。启动失败时窗口会保留错误，日志位于 `%LOCALAPPDATA%\MouseOrbit\logs`。

只配置环境（不启动程序），或编译验证但不启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-MouseOrbit.ps1 -SetupOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-MouseOrbit.ps1 -NoLaunch
```

启动器安装的 SDK 仅供本程序使用。如果需要在终端手动开发，可使用以下路径（ARM64 电脑将 `x64` 换成 `arm64`），或使用已安装到系统的 .NET 10 SDK：

```powershell
& "$env:LOCALAPPDATA\MouseOrbit\dotnet\x64\dotnet.exe" run --project src/MouseOrbit -c Release
```

系统已配置 .NET 10 SDK 时，也可以运行：

```powershell
dotnet run --project src/MouseOrbit -c Release
```

## 验证

```powershell
dotnet run --project tests/MouseOrbit.Checks -c Release -- --logic
```

Windows 集成回归和本机性能对照（只操作测试窗口）：

```powershell
dotnet run --project tests/MouseOrbit.Checks -c Release -- --desktop
dotnet run --project tests/MouseOrbit.Checks -c Release -- --perf
```

仍在迭代，手势识别和游戏兼容性需要实际体验。随项目使用的 Tiny5 字体许可证见 [OFL.txt](src/MouseOrbit/Assets/Fonts/OFL.txt)。
