using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public interface IForegroundWindowSource : IDisposable
{
    event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

    nint GetCurrentForegroundWindow();
}
