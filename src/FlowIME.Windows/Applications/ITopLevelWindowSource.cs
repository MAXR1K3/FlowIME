namespace FlowIME.Windows.Applications;

internal interface ITopLevelWindowSource
{
    IReadOnlyList<nint> EnumerateVisibleTopLevelWindows();
}
