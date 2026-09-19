using System.Diagnostics;

namespace FlowIME.Windows.Applications;

internal sealed class FileVersionDisplayNameResolver : IApplicationDisplayNameResolver
{
    public string Resolve(string executablePath, string processName)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }

            if (!string.IsNullOrWhiteSpace(info.ProductName))
            {
                return info.ProductName.Trim();
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        return string.IsNullOrWhiteSpace(processName)
            ? Path.GetFileNameWithoutExtension(executablePath)
            : processName;
    }
}
