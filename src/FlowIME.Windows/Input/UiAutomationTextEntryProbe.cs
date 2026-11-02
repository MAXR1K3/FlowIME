using System.Runtime.InteropServices;
using System.Windows.Automation;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

/// <summary>
/// Decides whether the focused element of a window can take typed text. Qt and
/// Chromium apps keep native focus on the top-level HWND, so UI Automation is the
/// only signal that tells a chat list or a read-only page apart from a text box.
/// Reads control metadata only, never values or document text.
/// </summary>
public sealed class UiAutomationTextEntryProbe : ITextEntryProbe
{
    // Control types that never accept typed text. Custom, ComboBox, Pane-like
    // containers of unknown editors and anything unlisted stay Unknown.
    private static readonly HashSet<ControlType> NonTextControlTypes =
    [
        ControlType.Button,
        ControlType.Calendar,
        ControlType.CheckBox,
        ControlType.DataItem,
        ControlType.Group,
        ControlType.Header,
        ControlType.HeaderItem,
        ControlType.Hyperlink,
        ControlType.Image,
        ControlType.List,
        ControlType.ListItem,
        ControlType.Menu,
        ControlType.MenuBar,
        ControlType.MenuItem,
        ControlType.Pane,
        ControlType.ProgressBar,
        ControlType.RadioButton,
        ControlType.ScrollBar,
        ControlType.Separator,
        ControlType.Slider,
        ControlType.SplitButton,
        ControlType.StatusBar,
        ControlType.Tab,
        ControlType.TabItem,
        ControlType.Table,
        ControlType.Text,
        ControlType.Thumb,
        ControlType.TitleBar,
        ControlType.ToolBar,
        ControlType.ToolTip,
        ControlType.Tree,
        ControlType.TreeItem,
        ControlType.Window
    ];

    public TextEntryPresence Probe(WindowContext window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (HasCaret(window.ThreadId))
        {
            return TextEntryPresence.Present;
        }

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || element.Current.ProcessId != window.ProcessId)
            {
                return TextEntryPresence.Unknown;
            }

            bool? isReadOnly = element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) &&
                value is ValuePattern valuePattern
                    ? valuePattern.Current.IsReadOnly
                    : null;
            return Classify(element.Current.ControlType, element.Current.IsEnabled, isReadOnly);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

        return TextEntryPresence.Unknown;
    }

    internal static TextEntryPresence Classify(
        ControlType controlType,
        bool isEnabled,
        bool? isReadOnly)
    {
        if (controlType == ControlType.Edit || controlType == ControlType.Document)
        {
            // Browser pages are read-only Documents; editors are writable ones.
            return isEnabled && isReadOnly != true
                ? TextEntryPresence.Present
                : TextEntryPresence.Absent;
        }

        // Some Qt containers expose a writable ValuePattern, so the pattern alone
        // is not text-box evidence: only the control type decides here.
        return NonTextControlTypes.Contains(controlType)
            ? TextEntryPresence.Absent
            : TextEntryPresence.Unknown;
    }

    private static bool HasCaret(uint threadId)
    {
        var info = new User32Native.GuiThreadInfo
        {
            CbSize = checked((uint)Marshal.SizeOf<User32Native.GuiThreadInfo>())
        };
        return threadId != 0 &&
            User32Native.GetGUIThreadInfo(threadId, ref info) &&
            info.HwndCaret != 0;
    }
}
