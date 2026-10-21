using MouseOrbit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class SpatialInteractionChecks
{
    private static IEnumerable<T> Children<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is UIElement element && element.Visibility != Visibility.Visible) continue;
            if (child is T value) yield return value;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    internal static void Run()
    {
        string original = Palette.Current.Id; int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; }
        using var live = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Spatial interaction fixture") { ParentWindow = new nint(-3), Width = 1, Height = 1 });
        foreach (string id in new[] { "typefield" })
        {
            Palette.Apply(id);
            using var stale = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Closed spatial fixture") { ParentWindow = new nint(-3), Width = 1, Height = 1 });
            var window = new SwitchWindow(live.Handle, (uint)Environment.ProcessId, "g0", "应用 0", "准确目标窗口", false, 1, 10);
            var groups = Enumerable.Range(0, 12).Select(i => new SwitchGroup("g" + i, "应用 " + i,
                i == 0 ? new[] { window, window with { Title = "第二窗口" }, window with { Handle = stale.Handle, Title = "第三窗口" } } : new[] { window with { AppKey = "g" + i, Title = "窗口 " + i } })).ToArray();
            groups[7] = groups[7] with { Windows = new[] { window with { AppKey = "g7", Title = "远端组窗口" }, window with { AppKey = "g7", Title = "远端组第二窗口" } } };
            var entries = Enumerable.Range(0, 13).Select(i => new DesktopEntry("e" + i, "文件 " + i, "", "command", "downloads", "")).ToArray();
            var panel = new QuickPanelWindow();
            void Render() => OffscreenPreview.Render((FrameworkElement)panel.Content, (int)panel.Width, (int)panel.Height);
            Button Button(string name) => Children<Button>((DependencyObject)panel.Content).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
            void Click(string name) { Button(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Render(); }
            try
            {
                panel.SetWindows(groups); panel.SetEntries(new() { ["frequent"] = entries, ["recent"] = entries }); Render();
                var view = panel.SpatialView!;
                Check(view != null, "spatial style owns a separate interaction view: " + id);
                Check(!panel.CreateMenu().Items.OfType<MenuItem>().Any(m => m.Header as string == "样式"), "single-style menu has no redundant appearance selector");
                var menu = panel.CreateMenu();
                Check(menu.Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(new[] { "窗口", "常用", "最近", "截图 / GIF", "设置" }), "particle wing follows the five-entry concept structure");
                Check(menu.Items.OfType<MenuItem>().Last().Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(new[] { "轮盘", "专注模式", "管理", "关闭" }), "secondary actions remain accessible under settings");
                if (id == "typefield")
                {
                    double Left(FrameworkElement item) => item.TransformToAncestor(view!).Transform(new Point()).X;
                    var apps = Enumerable.Range(0, 12).Select(i => Button("应用 " + i)).ToArray();
                    Check(apps.Select(Left).Distinct().Count() == 1, "K application rows share one left edge");
                    Check(apps.Select(b => Children<EntryIcon>(b).Single()).Select(Left).Distinct().Count() == 1, "K native icons share one column");
                    Check(apps.Select((b, i) => Children<TextBlock>(b).Single(t => t.Text == "应用 " + i)).Select(Left).Distinct().Count() == 1, "K names share one column regardless of window count");
                    Check(Left(Children<TextBlock>(apps[0]).Single(t => t.Text == "3")) == Left(Children<TextBlock>(apps[7]).Single(t => t.Text == "2")), "K window counts share one right-aligned column");
                }
                Check(Children<Button>((DependencyObject)panel.Content).Count(b => System.Windows.Automation.AutomationProperties.GetName(b) == "更多") == 1 &&
                    !Children<Button>((DependencyObject)panel.Content).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) is "切换列表" or "截图"), "top category chip and bottom tool strip are replaced by one content-side menu: " + id);
                double menuTop = view!.MenuAnchor.TransformToAncestor(view).Transform(new Point()).Y;
                view.FocusRow(Button("应用 1"), false); Render();
                Check(view.MenuAnchor.TransformToAncestor(view).Transform(new Point()).Y > menuTop && ReferenceEquals(panel.CreateMenu().PlacementTarget, view.MenuAnchor), "menu follows the pointed row and its side wing opens from that target");
                Check(Children<UiIcon>(Button("更多")).Single().Kind == "chevronRight", "inline fold replaces the detached ellipsis");
                string tool = ""; panel.ToolRequested += value => tool = value;
                panel.CreateMenu().Items.OfType<MenuItem>().Single(m => m.Header as string == "截图 / GIF").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(tool == "capture", "merged menu preserves the shared capture entry: " + id);
                var parent = Button("应用 0"); var before = parent.TransformToAncestor((Visual)panel.Content).Transform(new Point());
                var hit = VisualTreeHelper.HitTest(parent, new Point(2, 2))?.VisualHit;
                while (hit != null && hit != parent) hit = VisualTreeHelper.GetParent(hit);
                Check(hit == parent, "the full choice area responds, including the space outside its icon and lettering: " + id);
                Click("应用 0");
                var after = Button("应用 0").TransformToAncestor((Visual)panel.Content).Transform(new Point());
                Check(view!.State.Expanded == "g0" && Button("应用 1") != null, "expanding keeps the application context visible: " + id);
                Check(before == after, "expanding preserves the clicked application's screen location: " + id);
                Check(Button("准确目标窗口") != null && Button("第三窗口") != null, "child windows are directly addressable: " + id);
                if (id == "typefield")
                {
                    Check(Children<UiIcon>(Button("应用 0")).Any(icon => icon.RenderTransform is RotateTransform { Angle: 90 }), "K expanded group has a persistent downward indicator");
                    var childY = Button("准确目标窗口").TransformToAncestor((Visual)panel.Content).Transform(new Point()).Y;
                    var nextY = Button("应用 1").TransformToAncestor((Visual)panel.Content).Transform(new Point()).Y;
                    Check(childY > after.Y && nextY > childY, "K inserts windows below the parent and pushes later lines down");
                }
                Click("应用 0");
                Check(view.State.Expanded == null && ReferenceEquals(parent, Button("应用 0")), "clicking the same application collapses without replacing its control: " + id);
                Click("应用 0");
                void BlankClick() => view.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                int dismissed = 0; view.DismissRequested += () => dismissed++;
                BlankClick(); Render(); Check(view.State.Expanded == null && dismissed == 0, "blank click first collapses local context: " + id);
                BlankClick(); Check(dismissed == 1, "blank click dismisses after context is collapsed: " + id);
                panel.ScrollWheel(-120); Render();
                {
                    var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); Render();
                    Check(view.State.Page == 0 && Math.Abs(view.Flow.VerticalOffset - SpatialSwitcherView.RowHeight) < 1, "K scrolls one actual row in a continuous text flow rather than J group paging");
                }
                panel.SelectCategory("recent"); Render();
                int removed = 0; bool opened = false; panel.Selected += _ => opened = true; panel.RemoveRecentRequested = _ => { removed++; return true; };
                Click("从最近移除：文件 0");
                Check(removed == 1 && !opened && !Children<Button>((DependencyObject)panel.Content).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "文件 0"), "removing a recent item never activates it: " + id);
                panel.SelectCategory("windows"); Render(); Click("应用 0");
                Palette.SetBackdropDark(!Palette.Dark); Render();
                Check(panel.SpatialView!.State.Expanded == "g0" && Button("准确目标窗口") != null, "adaptive tone refresh preserves the expanded group: " + id);
                Palette.SetBackdropDark(false); Palette.Apply(id); Render();
                DesktopEntry? selected = null; panel.Selected += e => selected = e;
                stale.Dispose(); Click("第三窗口"); Check(selected == null, "stale child window is rejected in the new interaction view: " + id);
                Click("准确目标窗口");
                Check(selected?.Target == live.Handle.ToString() && selected.Kind == "window", "child selection carries the exact chosen native window: " + id);
                panel.SelectCategory("windows"); Render();
                var currentView = panel.SpatialView!; currentView.FocusRow(Button("应用 7"), false);
                Click("应用 7");
                Check(Math.Abs(currentView.MenuAnchor.TransformToAncestor(currentView).Transform(new Point()).Y - Button("应用 7").TransformToAncestor(currentView).Transform(new Point()).Y - (SpatialSwitcherView.RowHeight - 36) / 2) < .5, "expanding a later group keeps the side fold attached to that row");
                Palette.SetBackdropDark(!Palette.Dark); Render();
                Check(panel.SpatialView!.State.Expanded == "g7" && Button("远端组窗口") != null, "adaptive tone refresh preserves a later expanded group: " + id);
                Palette.SetBackdropDark(false); Palette.Apply(id); Render();
                foreach (var area in new[] { new Rect(0, 0, 1920, 1040), new Rect(-2560, -300, 2560, 1400), new Rect(0, 0, 280, 260) })
                foreach (double dpi in new[] { 1d, 1.5, 2, 3 })
                foreach (var at in new[] { area.TopLeft, area.BottomRight })
                    Check(area.Contains(SwitcherPlacement.Place(Palette.Layout, at.X, at.Y, panel.Width * dpi, panel.Height * dpi, area, dpi)), "new interaction placement stays inside the work area: " + id);
            }
            finally { panel.Close(); }
        }
        foreach (var mode in new[] { AppearanceLayout.Typefield })
        {
            for (int i = 0; i < 8; i++)
            {
                var p = SpatialWheel.LabelPosition(i);
                Check(SpatialWheel.Hit(mode, p.X - 210, p.Y - 210 + 15) == i, "visible command text belongs to its actual hit region: " + mode + " / " + i);
            }
            Check(SpatialWheel.Hit(mode, 0, 0) == -1 && SpatialWheel.Hit(mode, 800, -800) == -1, "center and far empty space cancel instead of executing an angular sector: " + mode);
            var gap = new Point(52, -126);
            Check(SpatialWheel.Hit(mode, gap.X, gap.Y) == -1, "empty gaps do not act as invisible direction buttons: " + mode);
        }
        Check(!SpatialWheel.HoldMoved(0, 0) && !SpatialWheel.HoldMoved(4, 4) && SpatialWheel.HoldMoved(16, 0), "stationary edge holds cancel independently of the repositioned wheel center");
        Palette.Apply(original);
        Console.WriteLine($"PASS: {count} spatial interaction checks (inline flow, adaptive tone state, actual shape hit targets, state transfer)");
    }
}
