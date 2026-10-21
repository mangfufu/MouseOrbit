using System.Threading.Tasks;
using System.Windows.Controls;
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
    private readonly GestureRenderer _graphics = new();
    private readonly ActionToast _feedback = new();
    private readonly PointerPath _path = new();
    private readonly TriggerPress _press = new();
    private readonly WindowCatalog _catalog = new();
    private readonly WindowSnapshotCache _windowCache;
    internal LauncherSession LauncherSession { get; } = new(new Launcher());
    private MainWindow? _manager;
    private TaskCompletionSource? _startupReady;
    private long _nextWindowRefresh;
    private readonly CaptureTools _capture = new();
    private readonly MouseInputBridge _inputBridge;
    private readonly GlobalInput _input;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly MenuItem _focusItem;
    private readonly ContextMenu _trayMenu;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly DispatcherTimer _timer;
    private bool _wheel, _heldTrigger, _heldRight, _consumeLeftUp, _gestureCanceled;
    private bool _consumeRightUp, _disposed, _autoFocus, _lastPaused, _wheelFromHold;
    private long _nextFocus, _gestureVersion;
    private int _triggerUp, _page, _wheelDelta;
    private Native.Point _origin, _latest;
    private double _gestureScale = 1;
    private double _triggerScale = 1;
    private uint _triggerButton;
    private StrokeGesture? _gesture;
    private WindowTarget _target;
    private nint _checkedForeground;
    private int _quickRequest, _lastGestureView = -1;
    private bool _quickPending;
    private bool _focusChecking;
    public nint LastTarget => _target.Handle;
    internal bool Interacting => _heldRight || _heldTrigger || _wheel || _quick.IsVisible || _quickPending || _capture.Busy || _inputBridge.HasPending;
    public Func<Dictionary<string, IReadOnlyList<DesktopEntry>>>? QuickEntries;
    public Func<DesktopEntry, bool>? RemoveRecentEntry;
    public Func<DesktopEntry, bool>? RemoveFrequentEntry;
    public event Action<DesktopEntry>? EntryOpened;
    public event Action? QuickOpened;
    public OrbitController()
    {
        _windowCache = new WindowSnapshotCache(() => _catalog.Read(0), groups => ShellIcons.PreloadAsync(groups.SelectMany(g => g.Windows).Select(w => w.Icon)));
        Palette.Apply(ThemePresets.Find(Settings.Theme, Settings.DarkTheme).Id);
        _target = WindowTarget.Capture(Native.GetForegroundWindow());
        Native.GetCursorPos(out _latest);
        _inputBridge = new MouseInputBridge(() => Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(DrainInput)), _latest);
        _quick.OpenStateChanged += PublishInputContext;
        _orbit.IsVisibleChanged += (_, _) => PublishInputContext();
        _quick.Selected += entry => _ = ExecuteEntry(entry);
        _quick.Manage += OpenSettings;
        _quick.ToolRequested += id => _ = Tool(id);
        _quick.RemoveRecentRequested = entry => RemoveRecentEntry?.Invoke(entry) == true;
        _quick.RemoveFrequentRequested = entry => RemoveFrequentEntry?.Invoke(entry) == true;
        QuickEntries = LauncherSession.BuildQuickEntries;
        RemoveRecentEntry = entry => LauncherSession.Remove(entry, false);
        RemoveFrequentEntry = entry => LauncherSession.Remove(entry, true);
        EntryOpened += LauncherSession.Record;
        LauncherSession.Changed += () => { if (!_disposed && !Interacting) PrepareAssistant(); };
        LauncherSession.Status += Report;
        LauncherSession.Start(Settings, () => _disposed || Paused || Interacting || _manager?.IsVisible == true);
        _trayIcon = Ui.TrayIcon(); _tray = new System.Windows.Forms.NotifyIcon { Icon = _trayIcon, Text = "MouseOrbit", Visible = true };
        var menu = Ui.Menu(_quick); _trayMenu = menu;
        void Add(string title, string icon, Action action)
        {
            var item = new MenuItem { Header = title, Icon = new UiIcon { Kind = icon, Width = 16, Height = 16 } };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("窗口切换", "window", () => OpenAssistant());
        _focusItem = new MenuItem { Header = "专注模式", IsChecked = Settings.ManualFocus, Icon = new UiIcon { Kind = "focus" } };
        _focusItem.Click += (_, _) => TogglePause(); menu.Items.Add(_focusItem);
        Add("截图 / GIF", "screenshot", () => _ = Tool("capture"));
        Add("设置", "settings", OpenSettings);
        Add("退出", "close", () => Application.Current.Shutdown());
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Right) { menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint; menu.IsOpen = true; } };
        _tray.DoubleClick += (_, _) => OpenAssistant();
        _capture.Error += Report;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        if (Preferences.LoadWarning != null) LastStatus = Preferences.LoadWarning;
        PublishInputContext();
        _input = new GlobalInput(_inputBridge.Handle);
        _graphics.Error += error => Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) Report(error); }));
    }
    private void Queue(Action action) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    { if (_disposed) return; action(); PublishInputContext(); }));
    private void PublishInputContext() => _inputBridge.Publish(new(!_disposed && !Paused && !_capture.Busy, Settings.Trigger, Settings.Gestures, _wheel, _quick.IsVisible || _quickPending));
    private void DrainInput()
    {
        var samples = _inputBridge.Drain(out bool overflowed);
        if (_disposed) return;
        if (overflowed)
        {
            // Never execute a partially dropped stroke after a long UI stall.
            Cancel(); _heldTrigger = _heldRight = _consumeLeftUp = _consumeRightUp = false; _gesture = null;
        }
        else foreach (var sample in samples) OnMouse(sample);
        _latest = _inputBridge.Latest; PublishInputContext();
    }
    private void Tick()
    {
        if (_disposed) return;
        _latest = _inputBridge.Latest;
        var foreground = Native.GetForegroundWindow(); _catalog.Observe(foreground);
        long now = Environment.TickCount64;
        if (_windowCache.Ready && _startupReady?.Task.IsCompleted != false && now >= _nextWindowRefresh && !_heldRight && !_heldTrigger && !_wheel)
        { _nextWindowRefresh = now + 3000; _ = RefreshWindowCache(); }
        if (!_focusChecking && (now >= _nextFocus || foreground != _checkedForeground))
        {
            _nextFocus = now + 250; _checkedForeground = foreground;
            _ = CheckFocus(foreground);
        }
        if (Paused != _lastPaused)
        {
            _lastPaused = Paused; if (Paused) Cancel();
            _focusItem.IsChecked = Settings.ManualFocus;
            _tray.Text = Paused ? (Settings.ManualFocus ? "MouseOrbit · 专注模式" : "MouseOrbit · 游戏／全屏专注") : "MouseOrbit";
            Report(Paused ? "专注模式" : "");
        }
        if (_inputBridge.HasPending) DrainInput();
        PublishInputContext();
        if (Paused || _capture.Busy) return;
        if (_heldTrigger && _press.TryHold(now, (_latest.X - _origin.X) / _triggerScale, (_latest.Y - _origin.Y) / _triggerScale))
            BeginWheel(_origin, true);
        if (_wheel)
        {
            if (_wheelFromHold && !HoldMoved(_latest, _origin))
            { if (_orbit.Visual.Selected != -1) { _orbit.Visual.Selected = -1; _orbit.Visual.InvalidateVisual(); } }
            else _orbit.Update(_latest);
        }
        if (_heldRight && !_gestureCanceled && _path.Points.Count > 1 && (_gesture?.VolumeMode == true || _lastGestureView != _path.Version))
        {
            _lastGestureView = _path.Version;
            string name = ""; bool global = false;
            if (_gesture?.HasMovement == true)
            {
                _gesture.TryEnterVolume();
                if (_gesture.VolumeMode)
                {
                    int steps = ApplyVolume(_gesture); global = true;
                    name = steps > 0 ? "音量 ↑" : steps < 0 ? "音量 ↓" : "音量 ↕";
                }
                else
                {
                    string shape = _gesture.Recognize(); var id = Settings.GesturesMap.GetValueOrDefault(shape);
                    name = id == null ? "继续划动" : Commands.Find(id).Name;
                    global = id is "desktop" or "volumeUp" or "volumeDown";
                }
            }
            _graphics.Update(_path.Points, _latest, _target, name, global);
        }
        PublishInputContext();
    }
    private async Task CheckFocus(nint foreground)
    {
        _focusChecking = true;
        bool enabled = Settings.AutoFocus; var applications = Settings.FocusApplications.ToArray();
        try
        {
            bool focused = await Task.Run(() => WindowNative.GameFocused(foreground, enabled, applications));
            if (_disposed) return;
            if (foreground != Native.GetForegroundWindow() || enabled != Settings.AutoFocus || !applications.SequenceEqual(Settings.FocusApplications)) { _nextFocus = 0; return; }
            _autoFocus = focused; PublishInputContext();
        }
        finally { _focusChecking = false; }
    }
    private bool OnMouse(MouseSample sample)
    {
        int message = sample.Message; var data = sample.Data;
        _latest = data.Pt;
        if (message == 0x202 && _consumeLeftUp) { _consumeLeftUp = false; return true; }
        if (message == 0x205 && _consumeRightUp) { _consumeRightUp = false; return true; }
        if (message == _triggerUp && _heldTrigger)
        {
            if (message == 0x20C && (data.Mouse >> 16) != _triggerButton) return false;
            _heldTrigger = false;
            _path.Clear(); _graphics.Clear();
            var released = _press.End(sample.Time, (data.Pt.X - _origin.X) / _triggerScale, (data.Pt.Y - _origin.Y) / _triggerScale);
            int version = _press.Version; var origin = _origin; var point = data.Pt;
            Queue(() =>
            {
                if (version != _press.Version) return;
                if (Paused || _capture.Busy) return;
                if (released == TriggerRelease.Panel) OpenQuick(origin);
                else if (released == TriggerRelease.Wheel)
                {
                    if (!_wheel) BeginWheel(origin, true);
                    _wheel = false;
                    // At a screen edge the wheel center moves inward. A stationary hold is still a cancellation.
                    bool moved = HoldMoved(point, origin);
                    if (moved) FinishWheel(point); else _orbit.Hide();
                }
                _wheelFromHold = false;
            }); return true;
        }
        if (message == 0x205 && _heldRight)
        {
            _gesture?.Add(data.Pt.X / _gestureScale, data.Pt.Y / _gestureScale, sample.Time);
            _heldRight = false; var gesture = _gesture; _gesture = null; var target = _target; bool canceled = _gestureCanceled;
            _path.Clear(); _graphics.Clear();
            long version = _gestureVersion;
            Queue(() =>
            {
                if (canceled || gesture == null || Paused || version != _gestureVersion) return;
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
        if (_heldTrigger && message is 0x201 or 0x204)
        {
            if (message == 0x201) _consumeLeftUp = true; else _consumeRightUp = true;
            Cancel(); return true;
        }
        if (_quick.IsVisible || _quickPending)
        {
            if (message == 0x20A) { _quick.ScrollWheel(unchecked((short)(data.Mouse >> 16))); return true; }
            if (message == 0x204) { _consumeRightUp = true; Queue(() => { CancelQuickOpen(); _quick.Hide(); }); return true; }
            if (message == 0x201 && !_quick.Contains(data.Pt)) Queue(() => { CancelQuickOpen(); _quick.Hide(); });
        }
        if (_heldRight && message == 0x201) { _consumeLeftUp = true; Cancel(); return true; }
        if (message == 0x200 && (_heldRight || _heldTrigger))
        {
            if (_heldRight) _path.Add(new(data.Pt.X, data.Pt.Y));
            if (_heldRight && !_gestureCanceled) _gesture?.Add(data.Pt.X / _gestureScale, data.Pt.Y / _gestureScale, sample.Time);
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
        bool trigger = sample.Start == MouseStart.Trigger;
        if (trigger && !_heldRight && !_heldTrigger)
        {
            _heldTrigger = true; _triggerUp = message == 0x207 ? 0x208 : 0x20C; _triggerButton = data.Mouse >> 16;
            _origin = data.Pt;
            _press.Begin(sample.Time, _quick.IsVisible || _wheel || _quickPending);
            _triggerScale = Math.Max(1, Native.GetDpiForWindow(sample.Target) / 96d);
            if (_quick.IsVisible || _wheel || _quickPending) Queue(() => { _wheel = false; _wheelFromHold = false; CancelQuickOpen(); _quick.Hide(); _orbit.Hide(); });
            _target = WindowTarget.Capture(sample.Target); return true;
        }
        if (sample.Start == MouseStart.Gesture && !_heldTrigger)
        {
            var window = sample.Target; _gestureVersion++; _lastGestureView = -1;
            _heldRight = true; _gestureCanceled = false; _target = WindowTarget.Capture(window);
            _graphics.Clear();
            _path.Begin(new(data.Pt.X, data.Pt.Y));
            _gestureScale = window == 0 ? 1 : Math.Max(1, Native.GetDpiForWindow(window) / 96d);
            _gesture = new StrokeGesture(data.Pt.X / _gestureScale, data.Pt.Y / _gestureScale, Settings.Sensitivity, sample.Time); return true;
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
        CancelQuickOpen();
        _quick.Hide(); _page = 0; _wheelDelta = 0; _wheel = true; _wheelFromHold = held;
        _orbit.Open(origin, Settings, holdToSelect: held, targetPinned: _target.Valid && DesktopActions.IsTopmost(_target.Handle));
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
            bool ok = DesktopActions.Execute(command, target.Handle);
            if (ok && id == "topmost")
            {
                bool pinned = DesktopActions.IsTopmost(target.Handle);
                Native.GetCursorPos(out var point); _feedback.NotifyPinned(pinned, point);
                Report(pinned ? "已置顶" : "已取消置顶");
            }
            else Report(ok ? command.Name : "操作未完成");
            return ok;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Report("操作失败：" + ex.Message); return false; }
    }
    public void OpenAssistant(bool captureTarget = true)
    {
        if (Paused || _capture.Busy) return;
        if (captureTarget && !WindowNative.IsOwn(Native.GetForegroundWindow())) _target = WindowTarget.Capture(Native.GetForegroundWindow());
        _wheel = false; _orbit.Hide(); Native.GetCursorPos(out var point); OpenQuick(point);
    }
    internal void BeginStartup()
    { _startupReady = new(TaskCreationOptions.RunContinuationsAsynchronously); _tray.Text = "MouseOrbit · 正在预读应用和图标"; }
    internal async Task PreloadWindowsAsync()
    {
        await _windowCache.EnsureReadyAsync();
        _nextWindowRefresh = Environment.TickCount64 + 3000;
    }
    internal void PrepareAssistant()
    {
        _quick.SetWindows(CachedWindows(Native.GetForegroundWindow()));
        _quick.Prepare(QuickEntries?.Invoke() ?? new());
    }
    internal void CompleteStartup()
    { _startupReady?.TrySetResult(); _tray.Text = Paused ? "MouseOrbit · 专注模式" : "MouseOrbit"; }
    private IReadOnlyList<SwitchGroup> CachedWindows(nint exclude) => WindowCatalog.Group(_windowCache.Current.SelectMany(group => group.Windows)
        .Where(window => WindowNative.IsWindow(window.Handle) && WindowNative.GetWindowThreadProcessId(window.Handle, out uint pid) != 0 && pid == window.Process), exclude);
    private async Task RefreshWindowCache()
    {
        try
        {
            await _windowCache.RefreshAsync();
            if (!_disposed && !Interacting) PrepareAssistant();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { if (!_disposed) Report("窗口列表更新失败：" + ex.Message); }
    }
    private void OpenQuick(Native.Point point)
    {
        if (Paused || _capture.Busy) return;
        _ = LoadQuick(point);
    }
    private async Task LoadQuick(Native.Point point)
    {
        int request = ++_quickRequest; _quickPending = true; PublishInputContext();
        try
        {
            if (_startupReady != null) await _startupReady.Task;
            await _windowCache.EnsureReadyAsync();
            if (_disposed || request != _quickRequest || Paused || _capture.Busy) return;
            var entries = QuickEntries?.Invoke() ?? new();
            var groups = CachedWindows(Native.GetForegroundWindow());
            if (_disposed || request != _quickRequest || Paused || _capture.Busy) return;
            _quick.SetWindows(groups); _quick.Open(point, entries); QuickOpened?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { if (!_disposed && request == _quickRequest) Report("窗口列表读取失败：" + ex.Message); }
        finally { if (request == _quickRequest) { _quickPending = false; PublishInputContext(); } }
    }
    private void CancelQuickOpen() { _quickRequest++; _quickPending = false; }
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
            var capture = _capture.Start(); PublishInputContext(); await capture; PublishInputContext(); return;
        }
        _quick.Hide(); await Execute(id, _target);
    }
    public async Task<bool> ExecuteFromAssistant(Command command)
    { Application.Current.MainWindow?.Hide(); return await Execute(command.Id, _target); }
    public void Cancel()
    {
        _wheel = false; _wheelFromHold = false; _gestureCanceled = true; _press.Cancel(); _gestureVersion++;
        CancelQuickOpen();
        if (_heldRight) { _heldRight = false; _consumeRightUp = true; _gesture = null; }
        _path.Clear(); _graphics.Clear();
        Queue(() => { _orbit.Hide(); _quick.Hide(); });
    }
    public void ChangeTheme(string? id = null)
    {
        var current = ThemePresets.Find(Settings.Theme, Settings.DarkTheme);
        var chosen = id == null ? ThemePresets.All[(Array.IndexOf(ThemePresets.All, current) + 1) % ThemePresets.All.Length] : ThemePresets.Find(id);
        Settings.Theme = chosen.Id; Settings.DarkTheme = chosen.Dark; Palette.Apply(chosen.Id); Save();
    }
    private bool HoldMoved(Native.Point point, Native.Point origin)
    {
        double dx = (point.X - origin.X) / _triggerScale, dy = (point.Y - origin.Y) / _triggerScale;
        return Palette.Spatial ? SpatialWheel.HoldMoved(dx, dy) : WheelMath.Sector(dx, dy, OrbitVisual.DeadZone) >= 0;
    }
    public void TogglePause() { Settings.ManualFocus = !Settings.ManualFocus; _focusItem.IsChecked = Settings.ManualFocus; if (Paused) Cancel(); Save(); Report(Settings.ManualFocus ? "专注模式" : ""); }
    public void Save()
    { PublishInputContext(); try { Settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report("设置保存失败：" + ex.Message); } }
    public void OpenSettings()
    {
        CancelQuickOpen(); _quick.Hide();
        if (_manager == null)
        {
            _manager = new MainWindow(this); Application.Current.MainWindow = _manager;
            _manager.Closed += (_, _) => { Application.Current.MainWindow = _quick; _manager = null; };
        }
        _manager.Show(); _manager.WindowState = WindowState.Normal; _manager.Activate(); PublishInputContext();
    }
    private void Report(string text) { LastStatus = text; Status?.Invoke(text); }
    public void Dispose()
    { _disposed = true; LauncherSession.Dispose(); _timer.Stop(); _input.Dispose(); _capture.Dispose(); _graphics.Dispose(); _feedback.Dispose(); _trayMenu.IsOpen = false; _tray.Visible = false; _tray.Dispose(); _trayIcon.Dispose(); _orbit.Close(); _quick.Close(); }
}
