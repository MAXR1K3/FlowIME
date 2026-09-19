using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class RollingFileTraceListenerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FlowIME.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Runtime_rotation_keeps_current_log_and_bounded_archives()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "automation.log");

        using (var listener = new RollingFileTraceListener(
                   path,
                   maxBytes: 48,
                   archiveCount: 2))
        {
            for (var index = 0; index < 12; index++)
            {
                listener.WriteLine($"entry-{index:D2}-abcdefghijklmnop");
            }
        }

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".1"));
        Assert.True(File.Exists(path + ".2"));
        Assert.False(File.Exists(path + ".3"));
    }

    [Fact]
    public void Zero_archive_retention_truncates_by_replacing_current_file()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "automation.log");

        using (var listener = new RollingFileTraceListener(
                   path,
                   maxBytes: 24,
                   archiveCount: 0))
        {
            listener.WriteLine("first-entry-is-long");
            listener.WriteLine("second-entry-is-long");
        }

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
        Assert.Contains(
            "second-entry",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_are_buffered_until_an_explicit_flush()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "automation.log");

        using var listener = new RollingFileTraceListener(path);
        listener.WriteLine("buffered-entry");

        Assert.Equal(string.Empty, ReadAllTextShared(path));

        listener.Flush();

        Assert.Contains(
            "buffered-entry",
            ReadAllTextShared(path),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Buffered_entries_are_flushed_on_the_configured_interval()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "automation.log");

        using var listener = new RollingFileTraceListener(
            path,
            flushInterval: TimeSpan.FromMilliseconds(20));
        listener.WriteLine("periodic-entry");

        Assert.True(SpinWait.SpinUntil(
            () => ReadAllTextShared(path).Contains("periodic-entry", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Snapshot_reports_buffering_flush_and_rotation_health()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "automation.log");

        using var listener = new RollingFileTraceListener(
            path,
            maxBytes: 24,
            archiveCount: 1,
            flushInterval: TimeSpan.FromHours(1));
        listener.WriteLine("first-entry-is-long");
        listener.WriteLine("second-entry-is-long");
        listener.Flush();

        var snapshot = listener.GetSnapshot();

        Assert.True(snapshot.Enabled);
        Assert.Equal(2, snapshot.EntryCount);
        Assert.True(snapshot.WrittenBytes > 0);
        Assert.Equal(1, snapshot.FlushCount);
        Assert.Equal(1, snapshot.RotationCount);
        Assert.Equal(0, snapshot.PendingBytes);
        Assert.Null(snapshot.LastErrorType);
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
