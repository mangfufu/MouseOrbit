# MouseOrbit

Windows 鼠标快捷工具，用侧键面板、轮盘和右键手势切换窗口、截图及调节音量。

## 使用

- 轻点侧键：打开窗口、常用应用和最近文件面板。
- 长按侧键：打开轮盘，滑向目标后松开执行。每个图标下都有功能名称。
- 按住右键：划手势，松开执行。左右分屏，上滑最大化／还原，下滑最小化，画圈切换桌面，C 复制，V 粘贴。
- 右滑后接着上下拖动：连续调节音量，无需停顿。
- 点“截图”：框选后可标注、复制、保存，也可选择 GIF 录制。GIF 录完先预览。

左右半屏再次触发同一方向会还原之前的窗口尺寸、位置和最大化状态；左右互换时仍保留最初状态。对启动前就已半屏、没有历史记录的窗口，再次触发同方向会退出半屏并居中。

轮盘保留分散的半透明选项，中心与间隙透明；鼠标悬停只改变颜色，透明度保持不变。已置顶的目标窗口在轮盘中显示“取消置顶”和状态圆点，操作后会短暂提示“已置顶／已取消置顶”。GIF 录制默认勾选“显示鼠标”，可关闭；录制以 20 帧/秒为目标，实际帧率随选区大小和编码耗时变化。结束后在录制区域附近打开紧凑预览，可暂停并用矩形、圆形、箭头、画笔、文字标注，支持颜色、粗细、字号、撤销与重做。标注应用于整段 GIF，保存保留帧尺寸和时间间隔。

支持多显示器、六套主题、常用与最近项目移除，以及自动／手动专注模式。默认侧键 1（后退），可在设置中改为侧键 2（前进）或中键。关闭面板后驻留托盘，从托盘退出。

鼠标输入使用独立线程接收，普通移动直接放行；界面异步处理按键与合并后的轨迹，界面繁忙不会阻塞鼠标钩子。窗口列表在后台扫描，预览使用 [DWM 实时缩略图](https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw)，不再请求其他应用执行 PrintWindow 截图；切页、滚动和关闭时同步裁剪或隐藏预览。

快速面板和管理面板会在启动时提前准备各页的卡片、模板、字体和布局；每页保留独立滚动区域，切页时直接复用。管理页的应用卡片按可见区域虚拟化。后台索引更新不会重建未打开的管理界面，自动专注检测与应用信息读取在后台执行。

启动时还会预读应用列表、窗口分组以及应用和文件图标，图标准备后才发布新索引。轻击侧键直接使用内存中的窗口列表，后台刷新期间继续使用已有数据。新增、移除或变化的项目只更新对应卡片，保留其他卡片；重新打开时不再把所有页面重新绘制成位图。图标缓存按项淘汰，不会在超过 512 项时整批清空。应用索引在闲置时更新，也可点“刷新”手动更新。

快速面板关闭时会裁去整个原生窗口区域，完全隐藏像素并让鼠标穿透，同时保留已准备的显示缓冲区；再次轻击侧键时恢复区域，减少重复隐藏和显示 WPF 窗口的开销。

手势图形在独立线程绘制，待绘制状态只保留最新一份；桌面合成暂时变慢时不会积压旧轨迹，也不会阻塞界面或鼠标钩子。轨迹使用小块原生绘制缓冲区，目标边框使用一个中空原生窗口。每次手势开始时轨迹和提示重新置顶，保持鼠标穿透并保留原应用焦点。

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
