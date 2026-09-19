using System.Runtime.InteropServices;
using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Context;

internal sealed record StandardTextControlObservation(
    bool IsTextEntry,
    ContextSignalConfidence Confidence,
    string Reason,
    nint FocusHwnd);

internal interface IStandardTextControlProbe
{
    StandardTextControlObservation Capture(ContextDetectionRequest request);
}

/// <summary>
/// Conservative, content-free text-control probe. It uses the WinEvent accessibility
/// object when available and falls back to well-known native edit HWND classes. It
/// never reads accName/accValue or any typed/document text.
/// </summary>
internal sealed class StandardTextControlProbe : IStandardTextControlProbe
{
    private static readonly Guid AccessibleInterfaceId =
        new("618736E0-3C3D-11CF-810C-00AA00389B71");

    public StandardTextControlObservation Capture(ContextDetectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var focusHwnd = ResolveFocusHwnd(request);
        if (focusHwnd == 0)
        {
            return NotText("focus-unavailable", focusHwnd);
        }

        var accessibility = TryReadAccessibility(request, focusHwnd);
        if (accessibility is not null)
        {
            return accessibility;
        }

        var className = ReadClassName(focusHwnd);
        if (IsNativeEditableClass(className))
        {
            return new StandardTextControlObservation(
                true,
                ContextSignalConfidence.High,
                "native-edit-class",
                focusHwnd);
        }

        return NotText("not-standard-text-control", focusHwnd);
    }

    private static StandardTextControlObservation? TryReadAccessibility(
        ContextDetectionRequest request,
        nint focusHwnd)
    {
        OleAccNative.IAccessibleDispatch? accessible = null;
        object? child = null;
        try
        {
            int hr;
            if (request.FocusObjectId != 0 || request.FocusChildId != 0)
            {
                hr = OleAccNative.AccessibleObjectFromEvent(
                    focusHwnd,
                    unchecked((uint)request.FocusObjectId),
                    unchecked((uint)request.FocusChildId),
                    out accessible,
                    out child);
            }
            else
            {
                var interfaceId = AccessibleInterfaceId;
                hr = OleAccNative.AccessibleObjectFromWindow(
                    focusHwnd,
                    OleAccNative.ObjIdClient,
                    ref interfaceId,
                    out accessible);
                child = 0;
            }

            if (hr < 0 || accessible is null)
            {
                return null;
            }

            var role = ConvertVariantToInt(accessible.get_accRole(child));
            var state = ConvertVariantToInt(accessible.get_accState(child));
            var isReadOnly = state.HasValue &&
                (state.Value & OleAccNative.StateSystemReadOnly) != 0;

            if (role == OleAccNative.RoleSystemText && !isReadOnly)
            {
                return new StandardTextControlObservation(
                    true,
                    ContextSignalConfidence.Certain,
                    "accessible-text",
                    focusHwnd);
            }

            if (role == OleAccNative.RoleSystemDocument && !isReadOnly)
            {
                return new StandardTextControlObservation(
                    true,
                    ContextSignalConfidence.Medium,
                    "accessible-document",
                    focusHwnd);
            }

            return new StandardTextControlObservation(
                false,
                ContextSignalConfidence.High,
                isReadOnly ? "accessible-readonly" : "accessible-non-text",
                focusHwnd);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (accessible is not null && Marshal.IsComObject(accessible))
            {
                _ = Marshal.ReleaseComObject(accessible);
            }
        }
    }

    private static nint ResolveFocusHwnd(ContextDetectionRequest request)
    {
        if (request.FocusHwnd != 0)
        {
            return request.FocusHwnd;
        }

        var info = new User32Native.GuiThreadInfo
        {
            CbSize = checked((uint)Marshal.SizeOf<User32Native.GuiThreadInfo>())
        };
        return User32Native.GetGUIThreadInfo(request.Window.ThreadId, ref info)
            ? info.HwndFocus
            : 0;
    }

    private static string ReadClassName(nint hwnd)
    {
        var buffer = new char[256];
        var count = User32Native.GetClassNameW(hwnd, buffer, buffer.Length);
        return count > 0 ? new string(buffer, 0, count) : string.Empty;
    }

    private static bool IsNativeEditableClass(string className) =>
        className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("RICHEDIT", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase);

    private static int? ConvertVariantToInt(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static StandardTextControlObservation NotText(string reason, nint focusHwnd) =>
        new(false, ContextSignalConfidence.Low, reason, focusHwnd);
}
