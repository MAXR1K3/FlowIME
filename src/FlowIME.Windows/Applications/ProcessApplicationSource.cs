using System.ComponentModel;
using System.Diagnostics;

namespace FlowIME.Windows.Applications;

internal sealed record ProcessApplicationEntry(
    uint ProcessId,
    string ProcessName,
    string ExecutablePath);

internal interface IProcessApplicationSource
{
    IReadOnlyList<ProcessApplicationEntry> Enumerate();
}

internal sealed class EmptyProcessApplicationSource : IProcessApplicationSource
{
    public IReadOnlyList<ProcessApplicationEntry> Enumerate() =>
        Array.Empty<ProcessApplicationEntry>();
}

internal sealed class SystemProcessApplicationSource : IProcessApplicationSource
{
    public IReadOnlyList<ProcessApplicationEntry> Enumerate()
    {
        var result = new List<ProcessApplicationEntry>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var processName = string.IsNullOrWhiteSpace(process.ProcessName)
                        ? Path.GetFileNameWithoutExtension(path)
                        : process.ProcessName;
                    result.Add(new ProcessApplicationEntry(
                        checked((uint)process.Id),
                        processName,
                        path));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A process can exit or deny query access between enumeration and inspection.
                }
            }
        }

        return result;
    }
}
