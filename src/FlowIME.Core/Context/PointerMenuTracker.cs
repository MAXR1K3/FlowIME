namespace FlowIME.Core.Context;

public enum PointerMenuTransition
{
    None,
    Enter,
    Exit
}

/// <summary>
/// Turns pointer visibility samples into menu-mode transitions. Many games show
/// their pointer in menus and hide it during play, but both states flicker for
/// a moment on page changes and kill cams, so each side must hold for a while
/// before it counts.
/// </summary>
public sealed class PointerMenuTracker
{
    public static readonly TimeSpan DefaultEnterAfter = TimeSpan.FromMilliseconds(1500);

    public static readonly TimeSpan DefaultExitAfter = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _enterAfter;
    private readonly TimeSpan _exitAfter;
    private bool? _showing;
    private DateTimeOffset _since;
    private bool _inMenu;

    public PointerMenuTracker()
        : this(DefaultEnterAfter, DefaultExitAfter)
    {
    }

    public PointerMenuTracker(TimeSpan enterAfter, TimeSpan exitAfter)
    {
        _enterAfter = enterAfter;
        _exitAfter = exitAfter;
    }

    public bool InMenu => _inMenu;

    public PointerMenuTransition Observe(bool pointerShowing, DateTimeOffset at)
    {
        if (_showing != pointerShowing)
        {
            _showing = pointerShowing;
            _since = at;
        }

        var held = at - _since;
        if (!_inMenu && pointerShowing && held >= _enterAfter)
        {
            _inMenu = true;
            return PointerMenuTransition.Enter;
        }

        if (_inMenu && !pointerShowing && held >= _exitAfter)
        {
            _inMenu = false;
            return PointerMenuTransition.Exit;
        }

        return PointerMenuTransition.None;
    }

    /// <summary>Forgets the pointer history, e.g. when a different window comes forward.</summary>
    public void Reset()
    {
        _showing = null;
        _inMenu = false;
    }
}
