# MouseOrbit

Windows 鼠标快捷工具，用侧键面板、轮盘和右键手势切换窗口、截图及调节音量。

## 使用

- 轻点侧键：打开窗口、常用应用和最近文件面板。
- 长按侧键：打开轮盘，滑向目标后松开执行。每个图标下都有功能名称。
- 按住右键：划手势，松开执行。左右分屏，上滑最大化／还原，下滑最小化，画圈切换桌面，C 复制，V 粘贴。
- 右滑后接着上下拖动：连续调节音量，无需停顿。
- 点“截图”：框选后可标注、复制、保存，也可选择 GIF 录制。GIF 录完先预览。

支持多显示器、六套主题、常用与最近项目移除，以及自动／手动专注模式。默认侧键 1，可在设置中改为侧键 2 或中键。关闭面板后驻留托盘，从托盘退出。

## 运行

需要 Windows 和 .NET 10 SDK。克隆后双击 `启动 MouseOrbit.cmd`，或运行：

```powershell
dotnet run --project src/MouseOrbit -c Release
```

## 验证

```powershell
dotnet run --project tests/MouseOrbit.Checks -c Release -- --logic
```

仍在迭代，手势识别和游戏兼容性需要实际体验。随项目使用的 Tiny5 字体许可证见 [OFL.txt](src/MouseOrbit/Assets/Fonts/OFL.txt)。
