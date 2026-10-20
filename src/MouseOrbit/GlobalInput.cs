using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MouseOrbit;

// Low-level hooks are synchronous with system input delivery. This message loop must never
// run WPF, window enumeration, process inspection, rendering, or Dispatcher.Invoke.
internal sealed class GlobalInput : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Native.Point Point; public uint Private; }
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, nint window, uint first, uint last);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private readonly Native.HookProc _mouseProc;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ConcurrentQueue<TaskCompletionSource> _probes = new();
    private uint _threadId;
    private nint _hook;
    private Exception? _startupError;
    private long _callbacks, _maximumTicks;
    private int _disposed, _callbackFailures;
    internal int ManagedThreadId => _thread.ManagedThreadId;
    internal bool IsAlive => _thread.IsAlive;
    internal long CallbackCount => Interlocked.Read(ref _callbacks);
    internal int CallbackFailures => Volatile.Read(ref _callbackFailures);
    internal double MaximumCallbackMilliseconds => Interlocked.Read(ref _maximumTicks) * 1000d / Stopwatch.Frequency;
    public GlobalInput(Func<int, Native.MouseData, bool> mouse)
    {
        _mouseProc = (code, message, data) =>
        {
            if (code >= 0)
            {
                long started = Stopwatch.GetTimestamp(); bool consumed = false;
                try
                {
                    var value = Marshal.PtrToStructure<Native.MouseData>(data);
                    if ((value.Flags & 1) == 0) consumed = mouse((int)message, value);
                }
                catch (Exception) { Interlocked.Increment(ref _callbackFailures); }
                finally
                {
                    Interlocked.Increment(ref _callbacks);
                    long ticks = Stopwatch.GetTimestamp() - started, previous;
                    do { previous = Interlocked.Read(ref _maximumTicks); if (ticks <= previous) break; }
                    while (Interlocked.CompareExchange(ref _maximumTicks, ticks, previous) != previous);
                }
                if (consumed) return 1;
            }
            return Native.CallNextHookEx(_hook, code, message, data);
        };
        _thread = new Thread(Run) { IsBackground = true, Name = "MouseOrbit mouse input" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        _ready.Wait();
        if (_startupError != null) { Dispose(); throw _startupError; }
    }
    private void Run()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0); // Create the queue before Dispose can post WM_QUIT.
            _hook = Native.SetWindowsHookEx(14, _mouseProc, Native.GetModuleHandle(null), 0);
            if (_hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法注册全局鼠标输入");
            _ready.Set();
            int result;
            while ((result = GetMessage(out var message, 0, 0, 0)) > 0)
            {
                if (message.Id == 0x8001 && _probes.TryDequeue(out var probe)) probe.TrySetResult();
            }
            if (result < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception ex) { _startupError = ex; }
        finally
        {
            if (_hook != 0) Native.UnhookWindowsHookEx(_hook);
            _hook = 0; _ready.Set();
            while (_probes.TryDequeue(out var probe)) probe.TrySetCanceled();
        }
    }
    internal Task ProbeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return Task.FromException(new ObjectDisposedException(nameof(GlobalInput)));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _probes.Enqueue(completion);
        if (!PostThreadMessage(_threadId, 0x8001, 0, 0)) completion.TrySetException(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        return completion.Task;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_threadId != 0) PostThreadMessage(_threadId, 0x12, 0, 0);
        if (Thread.CurrentThread != _thread && _thread.Join(1000)) _ready.Dispose();
    }
}
