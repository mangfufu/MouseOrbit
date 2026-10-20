using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MouseOrbit;

// Fixed-size cards with row virtualization. Recycling keeps app lists from building every icon/button at once.
public sealed class TileVirtualizingPanel : VirtualizingPanel, IScrollInfo
{
    private const double CellWidth = 144, CellHeight = 140;
    private double _offset;
    private Size _extent, _viewport;
    private int _columns = 1;
    protected override Size MeasureOverride(Size available)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        double width = double.IsInfinity(available.Width) ? Math.Max(CellWidth, ActualWidth) : Math.Max(1, available.Width);
        double height = double.IsInfinity(available.Height) ? Math.Max(CellHeight, ActualHeight) : Math.Max(1, available.Height);
        _columns = Math.Max(1, (int)(width / CellWidth));
        int count = owner?.Items.Count ?? 0;
        var extent = new Size(width, Math.Ceiling(count / (double)_columns) * CellHeight);
        var viewport = new Size(width, height);
        if (extent != _extent || viewport != _viewport)
        { _extent = extent; _viewport = viewport; _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent.Height - _viewport.Height)); ScrollOwner?.InvalidateScrollInfo(); }
        int first = Math.Max(0, ((int)(_offset / CellHeight) - 1) * _columns);
        int last = Math.Min(count - 1, ((int)Math.Ceiling((_offset + height) / CellHeight) + 1) * _columns - 1);
        _ = InternalChildren; // Ensure the items host has connected its generator before first measure.
        var generator = ItemContainerGenerator;
        if (generator == null) return new Size(width, height);
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0); int item = generator.IndexFromGeneratorPosition(position);
            if (item >= first && item <= last) continue;
            if (generator is IRecyclingItemContainerGenerator recycler) recycler.Recycle(position, 1); else generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
        if (last >= first)
        {
            var position = generator.GeneratorPositionFromIndex(first);
            int childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
            {
                for (int item = first; item <= last; item++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out bool created);
                    if (created || !InternalChildren.Contains(child))
                    {
                        if (childIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(CellWidth, CellHeight));
                }
            }
        }
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int item = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(item % _columns * CellWidth, item / _columns * CellHeight - _offset, CellWidth, CellHeight));
        }
        return finalSize;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        int index = args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move ? args.OldPosition.Index : args.Position.Index;
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        { if (InternalChildren.Count > 0) RemoveInternalChildRange(0, InternalChildren.Count); }
        else if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or
            System.Collections.Specialized.NotifyCollectionChangedAction.Replace or System.Collections.Specialized.NotifyCollectionChangedAction.Move)
        {
            if (args.ItemUICount > 0 && index >= 0 && index < InternalChildren.Count)
                RemoveInternalChildRange(index, Math.Min(args.ItemUICount, InternalChildren.Count - index));
        }
        InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int index)
    { SetVerticalOffset(index / _columns * CellHeight); }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void SetHorizontalOffset(double offset) { }
    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        offset = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (Math.Abs(offset - _offset) < .01) return;
        _offset = offset; ScrollOwner?.InvalidateScrollInfo(); InvalidateMeasure();
    }
    public void LineUp() => SetVerticalOffset(_offset - CellHeight / 3);
    public void LineDown() => SetVerticalOffset(_offset + CellHeight / 3);
    public void PageUp() => SetVerticalOffset(_offset - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offset - CellHeight);
    public void MouseWheelDown() => SetVerticalOffset(_offset + CellHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        DependencyObject? container = visual;
        while (container != null && VisualTreeHelper.GetParent(container) != this) container = VisualTreeHelper.GetParent(container);
        if (container == null) return Rect.Empty;
        int index = ItemsControl.GetItemsOwner(this).ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0) return Rect.Empty;
        double top = index / _columns * CellHeight;
        if (top < _offset) SetVerticalOffset(top);
        else if (top + CellHeight > _offset + _viewport.Height) SetVerticalOffset(top + CellHeight - _viewport.Height);
        return new Rect(index % _columns * CellWidth, top - _offset, CellWidth, CellHeight);
    }
}
