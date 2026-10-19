using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MouseOrbit;

internal sealed class OrbitController : IDisposable
{
    public Preferences Settings { get; } = Preferences.Load();
    public bool Paused => Settings.ManualFocus || _autoFocus;
    public bool ManualFocus => Settings.ManualFocus;
    public event Action<string>? Status;
    public string LastStatus { get; private set; } = "";
    private readonly OrbitWindow _orbit = new();
    private readonly QuickPanelWindow _quick = new();
    private readonly GestureHint _hint = new();
    private readonly PointerTrail _trail = new();
    private readonly TriggerPress _press = new();
    private readonly WindowCatalog _catalog = new();
    private readonly CaptureTools _capture = new();
    private readonly GlobalInput _input;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly System.Windows.Forms.ToolStripMenuItem _focusItem;
    private readonly DispatcherTimer _timer;
    private bool _wheel, _heldTrigger, _heldRight, _consumeLeftUp, _gestureCanceled;
    private bool _consumeRightUp, _disposed, _autoFocus, _lastPaused, _wheelFromHold;
    private long _nextFocus, _nextPreview;
    private int _triggerUp, _page, _wheelDelta;
    private Native.Point _origin, _latest;
    private double _gestureScale = 1;
    private double _triggerScale = 1;
    private uint _triggerButton;
    private StrokeGesture? _gesture;
    private WindowTarget _target;
    private nint _checkedForeground;
    public nint LastTarget => _target.Handle;
    public Func<Dictionary<string, IReadOnlyList<DesktopEntry>>>? QuickEntries;
    public Func<DesktopEntry, bool>? RemoveRecentEntry;
    public Func<DesktopEntry, bool>? RemoveFrequentEntry;
    public event Action<DesktopEntry>? EntryOpened;
    public event Action? QuickOpened;
    public OrbitController()
    {
        Palette.Apply(ThemePresets.Find(Settings.Theme, Settings.DarkTheme).Id);
        _target = WindowTarget.Capture(Native.GetForegroundWindow());
        _input = new GlobalInput { Mouse = OnMouse };
        _quick.Selected += entry => _ = ExecuteEntry(entry);
        _quick.Manage += OpenSettings;
        _quick.ToolRequested += id => _ = Tool(id);
        _quick.RemoveRecentRequested = entry => RemoveRecentEntry?.Invoke(entry) == true;
        _quick.RemoveFrequentRequested = entry => RemoveFrequentEntry?.Invoke(entry) == true;
        _tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "MouseOrbit", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("窗口切换", null, (_, _) => OpenAssistant());
        _focusItem = new System.Windows.Forms.ToolStripMenuItem("专注模式") { Checked = Settings.ManualFocus };
        _focusItem.Click += (_, _) => TogglePause(); menu.Items.Add(_focusItem);
        var themes = new System.Windows.Forms.ToolStripMenuItem("主题");
        foreach (var preset in ThemePresets.All)
        { string selected = preset.Id; themes.DropDownItems.Add(preset.Name, null, (_, _) => ChangeTheme(selected)); }
        menu.Items.Add(themes);
        menu.Items.Add("截图 / GIF", null, (_, _) => _ = Tool("capture"));
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("退出", null, (_, _) => Application.Current.Shutdown());
        _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => OpenAssistant();
        _capture.Error += Report;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        if (Preferences.LoadWarning != null) LastStatus = Preferences.LoadWarning;
    }
    private void Queue(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
    private void Tick()
    {
        if (_disposed) return;
        var foreground = Native.GetForegroundWindow(); _catalog.Observe(foreground);
        long now = Environment.TickCount64;
        if (now >= _nextFocus || foreground != _checkedForeground)
        {
            _nextFocus = now + 250; _checkedForeground = foreground;
            _autoFocus = WindowNative.GameFocused(foreground, Settings);
            if (Paused != _lastPaused)
            {
                _lastPaused = Paused; if (Paused) Cancel();
                _focusItem.Checked = Settings.ManualFocus;
                _tray.Text = Paused ? (Settings.ManualFocus ? "MouseOrbit · 专注模式" : "MouseOrbit · 游戏／全屏专注") : "MouseOrbit";
                Report(Paused ? "专注模式" : "");
            }
        }
        if (Paused || _capture.Busy) return;
        if (_heldTrigger && _press.TryHold(now, (_latest.X - _origin.X) / _triggerScale, (_latest.Y - _origin.Y) / _triggerScale))
            BeginWheel(_origin, true);
        if (_wheel)
        {
            if (_wheelFromHold && WheelMath.Sector((_latest.X - _origin.X) / _triggerScale, (_latest.Y - _origin.Y) / _triggerScale, 55) < 0)
            { if (_orbit.Visual.Selected != -1) { _orbit.Visual.Selected = -1; _orbit.Visual.InvalidateVisual(); } }
            else _orbit.Update(_latest);
        }
        if (_heldRight && !_gestureCanceled) _trail.Refresh();
        if (_heldRight && !_gestureCanceled && _gesture?.HasMovement == true)
        {
            _gesture.TryEnterVolume(); _trail.Refresh();
            if (_gesture.VolumeMode)
            {
                int steps = ApplyVolume(_gesture);
                _hint.Update(_latest, _target, steps > 0 ? "音量 ↑" : steps < 0 ? "音量 ↓" : "音量 ↕", true);
            }
            else
            {
                string shape = _gesture.Recognize();
                var id = Settings.GesturesMap.GetValueOrDefault(shape);
                _hint.Update(_latest, _target, id == null ? "继续划动" : Commands.Find(id).Name, id is "desktop" or "volumeUp" or "volumeDown");
            }
        }
        if (now >= _nextPreview && _quick.IsVisible == false && !_heldRight && !WindowNative.IsOwn(foreground))
        {
            _nextPreview = now + 5000;
            WindowNative.GetWindowThreadProcessId(foreground, out uint pid);
            _ = WindowPreviews.Load(new(foreground, pid, "", "", "", WindowNative.IsIconic(foreground), 1, 0));
        }
    }
    private bool FreshFullScreen()
    {
        var fg = Native.GetForegroundWindow();
        if (!Settings.AutoFocus || fg == _checkedForeground || WindowNative.IsOwn(fg) || WindowNative.IsDesktopSurface(fg) || !WindowNative.GetWindowRect(fg, out var box)) return false;
        var area = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
        return box.Left <= area.Left && box.Top <= area.Top && box.Right >= area.Right && box.Bottom >= area.Bottom;
    }
    private bool OnMouse(int message, Native.MouseData data)
    {
        _latest = data.Pt;
        if (message == 0x202 && _consumeLeftUp) { _consumeLeftUp = false; return true; }
        if (message == 0x205 && _consumeRightUp) { _consumeRightUp = false; return true; }
        if (message == _triggerUp && _heldTrigger)
        {
            if (message == 0x20C && (data.Mouse >> 16) != _triggerButton) return false;
            _heldTrigger = false;
            var released = _press.End(Environment.TickCount64, (data.Pt.X - _origin.X) / _triggerScale, (data.Pt.Y - _origin.Y) / _triggerScale);
            int version = _press.Version; var origin = _origin; var point = data.Pt;
            Queue(() =>
            {
                if (version != _press.Version) return;
                _trail.Clear();
                if (Paused || _capture.Busy) return;
                if (released == TriggerRelease.Panel) OpenQuick(origin);
                else if (released == TriggerRelease.Wheel)
                {
                    if (!_wheel) BeginWheel(origin, true);
                    _wheel = false;
                    // At a screen edge the wheel center moves inward. A stationary hold is still a cancellation.
                    bool moved = WheelMath.Sector((point.X - origin.X) / _triggerScale, (point.Y - origin.Y) / _triggerScale, 55) >= 0;
                    if (moved) FinishWheel(point); else _orbit.Hide();
                }
                _wheelFromHold = false;
            }); return true;
        }
        if (message == 0x205 && _heldRight)
        {
            _gesture?.Add(data.Pt.X / _gestureScale, data.Pt.Y / _gestureScale, Environment.TickCount64);
            _heldRight = false; var gesture = _gesture; _gesture = null; var target = _target; bool canceled = _gestureCanceled;
            Queue(() =>
            {
                _hint.Clear(); _trail.Clear();
                if (canceled || gesture == null || Paused) return;
                if (!gesture.HasMovement) { if (!Native.RightClick()) Report("右键菜单未能打开"); }
                else if (gesture.VolumeMode) { ApplyVolume(gesture, true); }
                else
                {
                    // A very fast right-turn may finish between two timer ticks.
                    if (gesture.TryEnterVolume()) { ApplyVolume(gesture, true); return; }
                    string shape = gesture.Recognize();
                    if (Settings.GesturesMap.TryGetValue(shape, out var id)) _ = Execute(id, target);
                    else Report("手势未识别，已取消");
                }
            }); return true;
        }
        if (Paused || _capture.Busy) return false;
        if (message is 0x204 or 0x20B or 0x207 && FreshFullScreen()) return false;
        if (_heldTrigger && message is 0x201 or 0x204)
        {
            if (message == 0x201) _consumeLeftUp = true; else _consumeRightUp = true;
            Cancel(); return true;
        }
        if (_quick.IsVisible)
        {
            if (message == 0x204) { _consumeRightUp = true; Queue(() => _quick.Hide()); return true; }
            if (message == 0x201 && !_quick.Contains(data.Pt)) Queue(() => _quick.Hide());
        }
        if (_heldRight && message == 0x201) { _consumeLeftUp = true; Cancel(); return true; }
        if (message == 0x200 && (_heldRight || _heldTrigger))
        {
            if (_heldRight) _trail.Path.Add(new(data.Pt.X, data.Pt.Y));
            if (_heldRight && !_gestureCanceled) _gesture?.Add(data.Pt.X / _gestureScale, data.Pt.Y / _gestureScale, Environment.TickCount64);
            return false;
        }
        if (_heldTrigger && !_wheel && message == 0x20A) return true;
        if (_wheel && message == 0x20A)
        {
            _wheelDelta += unchecked((short)(data.Mouse >> 16));
            // The wheel has one page. Consume scrolling without leaking it to the foreground app.
            _wheelDelta = 0; return true;
        }
        if (_wheel && message == 0x201)
        {
            _consumeLeftUp = true;
            if (_wheelFromHold) { Cancel(); return true; }
            _wheel = false; var point = data.Pt; Queue(() => FinishWheel(point)); return true;
        }
        if (_wheel && message == 0x204) { _consumeRightUp = true; Cancel(); return true; }
        bool trigger = Settings.Trigger switch
        {
            "XButton1" => message == 0x20B && (data.Mouse >> 16) == 1,
            "XButton2" => message == 0x20B && (data.Mouse >> 16) == 2,
            "Middle" => message == 0x207, _ => false
        };
        if (trigger && !_heldRight)
        {
            _heldTrigger = true; _triggerUp = Settings.Trigger == "Middle" ? 0x208 : 0x20C; _triggerButton = data.Mouse >> 16;
            _origin = data.Pt;
            _press.Begin(Environment.TickCount64, _quick.IsVisible || _wheel);
            _triggerScale = Math.Max(1, Native.GetDpiForWindow(Native.GetForegroundWindow()) / 96d);
            if (_quick.IsVisible || _wheel) Queue(() => { _wheel = false; _wheelFromHold = false; _quick.Hide(); _orbit.Hide(); });
            _target = WindowTarget.Capture(Native.GetForegroundWindow()); return true;
        }
        if (message == 0x204 && Settings.Gestures && !_heldTrigger)
        {
            var window = WindowNative.RootAt(data.Pt);
            if (WindowNative.IsOwn(WindowNative.GetAncestor(WindowNative.WindowFromPoint(data.Pt), 2))) return false;
            _heldRight = true; _gestureCanceled = false; _target = WindowTarget.Capture(window);
            _trail.Path.Begin(new(data.Pt.X, data.Pt.Y));
            _gestureScale = window == 0 ? 1 : Math.Max(1, Native.GetDpiForWindow(window) / 96d);
            _gesture = new StrokeGesture(data.Pt.X / _gestureScale, data.Pt.Y / _gestureScale, Settings.Sensitivity, Environment.TickCount64); return true;
        }
        return false;
    }
    public void ShowWheel()
    {
        if (Paused || _capture.Busy || _heldRight) return;
        if (_wheel) { Cancel(); return; }
        Native.GetCursorPos(out _latest); _origin = _latest;
        if (WindowNative.IsOwn(Native.GetForegroundWindow()) == false) _target = WindowTarget.Capture(Native.GetForegroundWindow());
        BeginWheel(_latest, false);
    }
    private void BeginWheel(Native.Point origin, bool held)
    {
        _quick.Hide(); _page = 0; _wheelDelta = 0; _wheel = true; _wheelFromHold = held;
        _orbit.Open(origin, Settings, holdToSelect: held);
    }
    private static int ApplyVolume(StrokeGesture gesture, bool finish = false)
    {
        int total = 0, steps;
        do
        {
            steps = gesture.TakeVolumeSteps(); total += steps;
            for (int i = 0; i < Math.Abs(steps); i++) Native.SendKeys(new ushort[] { steps > 0 ? (ushort)0xAF : (ushort)0xAE });
        } while (finish && steps != 0 && Math.Abs(total) < 50);
        return total;
    }
    private void FinishWheel(Native.Point point)
    { _orbit.Update(point); int selected = _orbit.Visual.Selected; _orbit.Hide(); if (selected >= 0) _ = Execute(Settings.GetWheel(_page)[selected], _target); }
    private async Task<bool> Execute(string id, WindowTarget target)
    {
        if (Paused || _capture.Busy) return false;
        var command = Commands.Find(id);
        if (id == "screenshot") { await Tool("capture"); return true; }
        if (command.Kind == CommandKind.Assistant) { OpenAssistant(false); return true; }
        for (int i = 0; i < 100; i++)
        {
            if (_disposed || Paused) return false;
            if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02, 0x04, 0x05, 0x06 }.All(k => (Native.GetAsyncKeyState(k) & 0x8000) == 0)) break;
            if (i == 99) { Report("请先松开鼠标按键"); return false; } await Task.Delay(10);
        }
        bool global = id is "desktop" or "taskView" or "switchApp" or "volumeUp" or "volumeDown" or "mute" or "playPause" or "nextTrack" or "previousTrack";
        if (!global && command.Kind != CommandKind.Launch && !target.Valid) { Report("目标窗口已关闭，已取消"); return false; }
        if (command.Kind == CommandKind.Window && WindowNative.IsDesktopSurface(target.Handle)) { Report("请在应用窗口上使用此操作"); return false; }
        if (id is "copy" or "paste")
        {
            if (Native.GetForegroundWindow() != target.Handle) { DesktopActions.Activate(target.Handle); await Task.Delay(80); }
            if (!target.ClipboardReady()) { Report("请先在目标窗口选中内容或放好光标"); return false; }
        }
        else if (command.Kind == CommandKind.Keys && !global && Native.GetForegroundWindow() != target.Handle) { Report("目标窗口已变化，已取消"); return false; }
        try
        {
            bool ok = DesktopActions.Execute(command, target.Handle); Report(ok ? command.Name : "操作未完成"); return ok;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Report("操作失败：" + ex.Message); return false; }
    }
    public void OpenAssistant(bool captureTarget = true)
    {
        if (Paused || _capture.Busy) return;
        if (captureTarget && !WindowNative.IsOwn(Native.GetForegroundWindow())) _target = WindowTarget.Capture(Native.GetForegroundWindow());
        _wheel = false; _orbit.Hide(); Native.GetCursorPos(out var point); OpenQuick(point);
    }
    private void OpenQuick(Native.Point point)
    {
        if (Paused || _capture.Busy) return;
        var current = Native.GetForegroundWindow();
        _quick.SetWindows(_catalog.Read(current)); _quick.Open(point, QuickEntries?.Invoke() ?? new()); QuickOpened?.Invoke();
    }
    private async Task ExecuteEntry(DesktopEntry entry)
    {
        try
        {
            if (entry.Kind == "command") { if (!await Execute(entry.Target, _target)) return; }
            else { Launcher.Open(entry); Report(""); }
            EntryOpened?.Invoke(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { _quick.Message("打开失败：" + ex.Message); }
    }
    private async Task Tool(string id)
    {
        if (id.StartsWith("theme:")) { ChangeTheme(id[6..]); return; }
        if (id == "theme") { ChangeTheme(); return; }
        if (id == "focus") { TogglePause(); return; }
        if (Paused || _capture.Busy) return;
        if (id == "wheel") { ShowWheel(); return; }
        if (id is "capture" or "gif")
        {
            Cancel(); Application.Current.MainWindow?.Hide(); await Task.Delay(160);
            await _capture.Start(); return;
        }
        _quick.Hide(); await Execute(id, _target);
    }
    public async Task<bool> ExecuteFromAssistant(Command command)
    { Application.Current.MainWindow.Hide(); return await Execute(command.Id, _target); }
    public void Cancel()
    {
        _wheel = false; _wheelFromHold = false; _gestureCanceled = true; _press.Cancel();
        if (_heldRight) { _heldRight = false; _consumeRightUp = true; _gesture = null; }
        Queue(() => { _orbit.Hide(); _quick.Hide(); _hint.Clear(); _trail.Clear(); });
    }
    public void ChangeTheme(string? id = null)
    {
        var current = ThemePresets.Find(Settings.Theme, Settings.DarkTheme);
        var chosen = id == null ? ThemePresets.All[(Array.IndexOf(ThemePresets.All, current) + 1) % ThemePresets.All.Length] : ThemePresets.Find(id);
        Settings.Theme = chosen.Id; Settings.DarkTheme = chosen.Dark; Palette.Apply(chosen.Id); Save();
    }
    public void TogglePause() { Settings.ManualFocus = !Settings.ManualFocus; _focusItem.Checked = Settings.ManualFocus; if (Paused) Cancel(); Save(); Report(Settings.ManualFocus ? "专注模式" : ""); }
    public void Save()
    { try { Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report("设置保存失败：" + ex.Message); } }
    public void OpenSettings() { _quick.Hide(); Application.Current.MainWindow.Show(); Application.Current.MainWindow.WindowState = WindowState.Normal; Application.Current.MainWindow.Activate(); }
    private void Report(string text) { LastStatus = text; Status?.Invoke(text); }
    public void Dispose()
    { _disposed = true; _timer.Stop(); _input.Dispose(); _capture.Dispose(); _hint.Dispose(); _trail.Dispose(); _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _orbit.Close(); _quick.Close(); }
}
