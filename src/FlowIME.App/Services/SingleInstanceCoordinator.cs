namespace FlowIME.App.Services;

/// <summary>
/// Keeps FlowIME single-instance without tying process activation to the UI layer.
/// A second process can ask the primary process to show its window or shut down.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultMutexName = @"Local\FlowIME.SingleInstance";
    private const string DefaultShowEventName = @"Local\FlowIME.ShowMainWindow";
    private const string DefaultExitEventName = @"Local\FlowIME.Exit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _exitEvent;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private Task? _listenerTask;
    private Action? _showRequested;
    private Action? _exitRequested;
    private bool _ownsMutex;
    private bool _disposed;

    internal SingleInstanceCoordinator(
        string mutexName = DefaultMutexName,
        string showEventName = DefaultShowEventName,
        string exitEventName = DefaultExitEventName)
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

        _exitEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            exitEventName);

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

    internal void StartListening(Action showRequested, Action? exitRequested = null)
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
        _exitRequested = exitRequested;
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

    internal void SignalPrimaryExit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary)
        {
            _exitEvent.Set();
        }
    }

    private void ListenLoop()
    {
        var waitHandles = new WaitHandle[]
        {
            _showEvent,
            _exitEvent,
            _listenerCancellation.Token.WaitHandle
        };

        while (!_listenerCancellation.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(waitHandles);
            if (signaled == 2)
            {
                return;
            }

            try
            {
                if (signaled == 0)
                {
                    _showRequested?.Invoke();
                }
                else
                {
                    _exitRequested?.Invoke();
                }
            }
            catch
            {
                // A failed activation or shutdown request must not kill the listener.
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
        _exitEvent.Dispose();
        _mutex.Dispose();
    }
}
