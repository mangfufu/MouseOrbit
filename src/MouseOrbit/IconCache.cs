using System.Threading.Tasks;
using System.Windows.Media;

namespace MouseOrbit;

// Completed icons are evicted individually. Never clear a whole application index
// or evict work in flight and enqueue the same shell extraction again.
internal sealed class IconCache
{
    private sealed record Item(Lazy<Task<ImageSource?>> Image, LinkedListNode<string> Node);
    private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _order = new();
    private readonly object _gate = new();
    private readonly int _capacity;
    internal IconCache(int capacity = 2048) => _capacity = Math.Max(1, capacity);
    internal Task<ImageSource?> Get(string key, Func<Task<ImageSource?>> load)
    {
        Item item;
        bool created = false;
        lock (_gate)
        {
            if (_items.TryGetValue(key, out item!)) { _order.Remove(item.Node); _order.AddLast(item.Node); }
            else
            {
                item = new(new Lazy<Task<ImageSource?>>(load), _order.AddLast(key)); _items[key] = item;
                created = true;
                Trim();
            }
        }
        var task = item.Image.Value;
        if (created) _ = task.ContinueWith(_ => { lock (_gate) Trim(); }, System.Threading.CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    internal bool TryGet(string key, out ImageSource? image)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var item) && item.Image.IsValueCreated && item.Image.Value.IsCompletedSuccessfully)
            {
                image = item.Image.Value.Result; _order.Remove(item.Node); _order.AddLast(item.Node); return true;
            }
        }
        image = null; return false;
    }
    private void Trim()
    {
        for (var node = _order.First; _items.Count > _capacity && node != null;)
        {
            var next = node.Next; var item = _items[node.Value];
            if (item.Image.IsValueCreated && item.Image.Value.IsCompleted) { _items.Remove(node.Value); _order.Remove(node); }
            node = next;
        }
    }
}
