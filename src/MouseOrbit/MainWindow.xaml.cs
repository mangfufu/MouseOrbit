using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MouseOrbit;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly OrbitController _controller;
    private readonly List<ComboBox> _wheelCombos = new();
    private readonly Dictionary<string, ComboBox> _gestureCombos = new();
    private string[][] _wheelDraft = [[], [], []];
    private int _editingPage;
    private bool _ready;
    private readonly Preferences _viewSettings;
    // Layout-only constructor for the designer and offscreen rendering. No global input is installed.
    internal MainWindow(Preferences settings)
    {
        _controller = null!;
        _viewSettings = settings;
        Palette.Resources(Resources);
        InitializeComponent();
        Ui.Install(this);
        Palette.Changed += ThemeChanged;
        Closed += (_, _) => Palette.Changed -= ThemeChanged;
        _wheelDraft = Enumerable.Range(0, 3).Select(p => settings.GetWheel(p).ToArray()).ToArray();
        Preview.Settings = settings;
        TriggerCombo.SelectedIndex = settings.Trigger switch { "XButton2" => 1, "Middle" => 2, _ => 0 };
        GesturesCheck.IsChecked = settings.Gestures;
        LearnCheck.IsChecked = settings.LearnApplications;
        AutoFocusCheck.IsChecked = settings.AutoFocus;
        SensitivitySlider.Value = settings.Sensitivity;
        _ready = true;
        LoadWheelPage(0);
        foreach (var pair in settings.GesturesMap)
            _gestureCombos[pair.Key] = AddRow(GestureRows, GestureRecognizer.Arrows(pair.Key), pair.Value);
        LoadFocusApps();
    }
    internal MainWindow(OrbitController controller) : this(controller.Settings)
    {
        _controller = controller;
        StatusText.Text = Preferences.LoadWarning ?? "";
        controller.Status += UpdateStatus;
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { Focus(); ContentScroll.ScrollToTop(); }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        controller.QuickEntries = BuildQuickEntries;
        controller.RemoveRecentEntry = RemoveRecentEntry;
        controller.RemoveFrequentEntry = RemoveFrequentEntry;
        BuildQuickEntries();
        IsVisibleChanged += (_, _) => { if (IsVisible && _resultsDirty) RefreshResults(); };
        controller.EntryOpened += entry => RecordQuickUsage(entry);
        _usageTimer.Tick += (_, _) => { ObserveUsage(); RefreshIndexIfStale(); PrepareChangedQuickPage(); }; _usageTimer.Start();
    }
    private void LoadWheelPage(int page)
    {
        _editingPage = page; WheelRows.Children.Clear(); _wheelCombos.Clear();
        string[] directions = ["↑ 上", "↗ 右上", "→ 右", "↘ 右下", "↓ 下", "↙ 左下", "← 左", "↖ 左上"];
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            var combo = AddRow(WheelRows, directions[i], _wheelDraft[page][i]); _wheelCombos.Add(combo);
            combo.SelectionChanged += (_, _) => { _wheelDraft[_editingPage][index] = combo.SelectedValue as string ?? "assistant"; Preview.Settings = Snapshot(); Preview.InvalidateVisual(); };
        }
        Preview.Page = page; Preview.Settings = Snapshot(); Preview.InvalidateVisual();
    }
    private void WheelPage_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) LoadWheelPage(Math.Max(0, WheelPageCombo.SelectedIndex)); }
    private ComboBox AddRow(StackPanel parent, string label, string id)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Palette.Muted });
        var combo = new ComboBox { ItemsSource = Commands.All, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = id };
        System.Windows.Automation.AutomationProperties.SetName(combo, label + "操作");
        Grid.SetColumn(combo, 1); row.Children.Add(combo); parent.Children.Add(row); return combo;
    }
    private Preferences Snapshot() => new()
    {
        Trigger = new[] { "XButton1", "XButton2", "Middle" }[Math.Max(0, TriggerCombo.SelectedIndex)],
        Gestures = GesturesCheck.IsChecked == true,
        LearnApplications = LearnCheck.IsChecked == true,
        Sensitivity = SensitivitySlider.Value,
        Wheel = _wheelDraft[0].ToArray(), WindowWheel = _wheelDraft[1].ToArray(), MediaWheel = _wheelDraft[2].ToArray(),
        GesturesMap = _gestureCombos.ToDictionary(p => p.Key, p => p.Value.SelectedValue as string ?? "back")
    };
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = Snapshot(); var settings = _controller.Settings;
        settings.Trigger = snapshot.Trigger; settings.Gestures = snapshot.Gestures;
        settings.LearnApplications = snapshot.LearnApplications;
        settings.AutoFocus = AutoFocusCheck.IsChecked == true;
        settings.Sensitivity = snapshot.Sensitivity; settings.Wheel = snapshot.Wheel; settings.GesturesMap = snapshot.GesturesMap;
        settings.WindowWheel = snapshot.WindowWheel; settings.MediaWheel = snapshot.MediaWheel;
        _controller.Save();
    }
    private void UpdateStatus(string text)
    {
        StatusText.Text = text; RunningLabel.Text = _controller.Paused ? "专注中" : "运行中";
        PauseButton.Content = _controller.ManualFocus ? "退出专注" : "专注模式";
        LoadFocusApps();
        AssistantStatus.Text = text;
    }
    private void Preview_Click(object sender, RoutedEventArgs e) => _controller.ShowWheel();
    private void Pause_Click(object sender, RoutedEventArgs e) => _controller.TogglePause();
    private void ThemeChanged() { Palette.Resources(Resources); Preview.InvalidateVisual(); }
    private void LoadFocusApps()
    {
        FocusAppRows.Children.Clear();
        foreach (string path in _viewSettings.FocusApplications.ToArray())
        {
            string selected = path;
            var button = Palette.Button(System.IO.Path.GetFileNameWithoutExtension(path) + "  ×", () =>
            { _viewSettings.FocusApplications.Remove(selected); _controller?.Save(); LoadFocusApps(); }, "移除自动专注应用");
            button.Margin = new Thickness(0, 0, 6, 6); FocusAppRows.Children.Add(button);
        }
    }
    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Title_Drag(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is Button) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void Sensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (SensitivityText != null) SensitivityText.Text = $"{e.NewValue:0} px"; }
}
