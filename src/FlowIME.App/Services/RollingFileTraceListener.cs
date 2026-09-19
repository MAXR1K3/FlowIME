using System.Diagnostics;
using System.Text;

namespace FlowIME.App.Services;

internal sealed record RollingFileTraceListenerSnapshot(
    bool Enabled,
    long EntryCount,
    long WrittenBytes,
    long PendingBytes,
    long FlushCount,
    long RotationCount,
    string? LastErrorType);

/// <summary>
/// A small bounded UTF-8 trace sink used for FlowIME's native automation logs.
/// Writes stay in the StreamWriter buffer on the caller's hot path and are flushed
/// periodically, while rotation keeps the resident log set bounded.
/// </summary>
internal sealed class RollingFileTraceListener : TraceListener
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _archiveCount;
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly TimeSpan _flushInterval;
    private readonly Timer _flushTimer;

    private StreamWriter? _writer;
    private bool _flushScheduled;
    private long _currentBytes;
    private long _unflushedBytes;
    private long _entryCount;
    private long _writtenBytes;
    private long _flushCount;
    private long _rotationCount;
    private string? _lastErrorType;
    private bool _faulted;
    private bool _disposed;

    internal RollingFileTraceListener(
        string path,
        long maxBytes = 2 * 1024 * 1024,
        int archiveCount = 3,
        TimeSpan? flushInterval = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A log path is required.", nameof(path));
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (archiveCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(archiveCount));
        }

        var effectiveFlushInterval = flushInterval ?? TimeSpan.FromMilliseconds(250);
        if (effectiveFlushInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(flushInterval));
        }

        _path = path;
        _maxBytes = maxBytes;
        _archiveCount = archiveCount;
        _flushInterval = effectiveFlushInterval;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        OpenWriter();
        _flushTimer = new Timer(
            static state => ((RollingFileTraceListener)state!).Flush(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public override void Write(string? message) => WriteCore(message ?? string.Empty);

    public override void WriteLine(string? message) =>
        WriteCore((message ?? string.Empty) + Environment.NewLine);

    internal RollingFileTraceListenerSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new RollingFileTraceListenerSnapshot(
                Enabled: !_faulted && !_disposed,
                EntryCount: _entryCount,
                WrittenBytes: _writtenBytes,
                PendingBytes: _unflushedBytes,
                FlushCount: _flushCount,
                RotationCount: _rotationCount,
                LastErrorType: _lastErrorType);
        }
    }

    internal bool FlushScheduledForTest
    {
        get
        {
            lock (_sync)
            {
                return _flushScheduled;
            }
        }
    }

    public override void Flush()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _flushScheduled = false;
            _ = _flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (_unflushedBytes == 0)
            {
                return;
            }

            try
            {
                _writer?.Flush();
                _unflushedBytes = 0;
                _flushCount++;
            }
            catch (IOException ex)
            {
                DisableAfterIoFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                DisableAfterIoFailure(ex);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _flushScheduled = false;
            if (disposing)
            {
                _flushTimer.Dispose();
                try
                {
                    _writer?.Dispose();
                }
                catch (IOException)
                {
                }
                finally
                {
                    _writer = null;
                    _unflushedBytes = 0;
                }
            }
        }

        base.Dispose(disposing);
    }

    private void WriteCore(string text)
    {
        lock (_sync)
        {
            if (_disposed || _faulted)
            {
                return;
            }

            try
            {
                var bytes = _encoding.GetByteCount(text);
                if (_currentBytes > 0 && _currentBytes + bytes > _maxBytes)
                {
                    Rotate();
                }

                _writer!.Write(text);
                _currentBytes += bytes;
                _unflushedBytes += bytes;
                _entryCount++;
                _writtenBytes += bytes;

                if (!_flushScheduled)
                {
                    _flushScheduled = true;
                    _ = _flushTimer.Change(_flushInterval, Timeout.InfiniteTimeSpan);
                }
            }
            catch (IOException ex)
            {
                DisableAfterIoFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                DisableAfterIoFailure(ex);
            }
        }
    }

    private void Rotate()
    {
        _rotationCount++;
        _writer?.Dispose();
        _writer = null;
        _unflushedBytes = 0;

        if (_archiveCount == 0)
        {
            DeleteIfExists(_path);
        }
        else
        {
            DeleteIfExists(ArchivePath(_archiveCount));

            for (var index = _archiveCount - 1; index >= 1; index--)
            {
                var source = ArchivePath(index);
                if (File.Exists(source))
                {
                    File.Move(source, ArchivePath(index + 1), overwrite: true);
                }
            }

            if (File.Exists(_path))
            {
                File.Move(_path, ArchivePath(1), overwrite: true);
            }
        }

        OpenWriter();
    }

    private void OpenWriter()
    {
        var stream = new FileStream(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        _writer = new StreamWriter(stream, _encoding)
        {
            AutoFlush = false
        };
        _currentBytes = stream.Length;
    }

    private void DisableAfterIoFailure(Exception error)
    {
        _faulted = true;
        _lastErrorType = error.GetType().Name;
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        _writer = null;
        _unflushedBytes = 0;
    }

    private string ArchivePath(int index) => $"{_path}.{index}";

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
