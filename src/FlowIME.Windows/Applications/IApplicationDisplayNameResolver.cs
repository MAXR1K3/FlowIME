namespace FlowIME.Windows.Applications;

internal interface IApplicationDisplayNameResolver
{
    string Resolve(string executablePath, string processName);
}
