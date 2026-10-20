using System.Threading.Tasks;

namespace MouseOrbit;

// UI-thread owner; the native scan runs in the pool. Opening the panel reads
// Current and never waits for a background refresh once startup has completed.
internal sealed class WindowSnapshotCache
{
    private readonly Func<IReadOnlyList<SwitchGroup>> _read;
    private readonly Func<IReadOnlyList<SwitchGroup>, Task>? _prepare;
    private Task? _pending;
    internal IReadOnlyList<SwitchGroup> Current { get; private set; } = [];
    internal bool Ready { get; private set; }
    internal WindowSnapshotCache(Func<IReadOnlyList<SwitchGroup>> read, Func<IReadOnlyList<SwitchGroup>, Task>? prepare = null)
    { _read = read; _prepare = prepare; }
    internal Task EnsureReadyAsync() => Ready ? Task.CompletedTask : RefreshAsync();
    internal Task RefreshAsync()
    {
        if (_pending is { IsCompleted: false }) return _pending;
        return _pending = ReadAsync();
    }
    private async Task ReadAsync()
    {
        var groups = await Task.Run(_read);
        if (_prepare != null) await _prepare(groups);
        Current = groups.Select(group => group with { Windows = group.Windows.ToArray() }).ToArray(); Ready = true;
    }
}
