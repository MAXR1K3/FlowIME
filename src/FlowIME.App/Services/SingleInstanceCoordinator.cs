namespace FlowIME.App.Services;

/// <summary>
/// Keeps FlowIME single-instance without tying process activation to the UI layer.
/// A second process only signals the primary process to show its existing window.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultMutexName = @"Local\FlowIME.SingleInstance";
    private const string DefaultShowEventName = @"Local\FlowIME.ShowMainWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private Task? _listenerTask;
    private Action? _showRequested;
    private bool _ownsMutex;
    private bool _disposed;

    internal SingleInstanceCoordinator(
        string mutexName = DefaultMutexName,
        string showEventName = DefaultShowEventName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "FlowIME single-instance coordination requires Windows.");
        }

        _showEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            showEventName);

        _mutex = new Mutex(initiallyOwned: false, mutexName);
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous process died without releasing the mutex. Windows grants
            // ownership to this waiter, so this process becomes the new primary.
            _ownsMutex = true;
        }
    }

    internal bool IsPrimary => _ownsMutex;

    internal void StartListening(Action showRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(showRequested);

        if (!IsPrimary)
        {
            throw new InvalidOperationException(
                "Only the primary FlowIME instance can listen for activation signals.");
        }

        if (_listenerTask is not null)
        {
            return;
        }

        _showRequested = showRequested;
        _listenerTask = Task.Run(ListenLoop);
    }

    internal void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary)
        {
            _showEvent.Set();
        }
    }

    private void ListenLoop()
    {
        var waitHandles = new WaitHandle[]
        {
            _showEvent,
            _listenerCancellation.Token.WaitHandle
        };

        while (!_listenerCancellation.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(waitHandles);
            if (signaled != 0)
            {
                return;
            }

            try
            {
                _showRequested?.Invoke();
            }
            catch
            {
                // A failed foreground restore must not kill the instance listener.
                // The next manual launch can signal the primary again.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation.Cancel();
        _showEvent.Set();

        if (_listenerTask is not null)
        {
            try
            {
                _listenerTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
            }
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _ownsMutex = false;
        }

        _listenerCancellation.Dispose();
        _showEvent.Dispose();
        _mutex.Dispose();
    }
}
