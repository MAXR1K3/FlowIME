using System.Runtime.InteropServices;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

public sealed class TsfLocalCompartmentInspector
{
    public TsfCompartmentSnapshot InspectCurrentThreadManager()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("TSF compartment inspection requires Windows.");
        }

        using var com = ComInitialization.EnterMta();
        object? instance = null;
        var activated = false;
        uint clientId = 0;

        try
        {
            var clsid = TsfInterop.ClsidThreadMgr;
            var iid = TsfInterop.IidThreadMgr;
            var createHr = TsfInterop.CoCreateInstance(
                in clsid,
                0,
                TsfInterop.ClsctxInprocServer,
                in iid,
                out instance);

            if (createHr < 0 || instance is not ITfThreadMgr threadManager)
            {
                return Failure(0, $"CoCreateInstance(CLSID_TF_ThreadMgr) failed: 0x{createHr:X8}");
            }

            var activateHr = threadManager.Activate(out clientId);
            if (activateHr < 0)
            {
                return Failure(clientId, $"ITfThreadMgr.Activate failed: 0x{activateHr:X8}");
            }

            activated = true;
            var compartmentManager = instance as ITfCompartmentMgr;
            if (compartmentManager is null)
            {
                return Failure(clientId, "ITfThreadMgr did not expose ITfCompartmentMgr.");
            }

            var openClose = ReadInt32(
                compartmentManager,
                TsfInterop.GuidCompartmentKeyboardOpenClose);
            var conversion = ReadInt32(
                compartmentManager,
                TsfInterop.GuidCompartmentKeyboardInputModeConversion);

            return new TsfCompartmentSnapshot(
                true,
                clientId,
                openClose,
                conversion,
                "FlowIME.Probe current thread manager only (not the target application's thread manager)",
                null);
        }
        finally
        {
            if (activated && instance is ITfThreadMgr threadManager)
            {
                _ = threadManager.Deactivate();
            }

            if (instance is not null && Marshal.IsComObject(instance))
            {
                _ = Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    private static int? ReadInt32(ITfCompartmentMgr manager, Guid compartmentId)
    {
        ITfCompartment? compartment = null;
        try
        {
            var hr = manager.GetCompartment(in compartmentId, out compartment);
            if (hr < 0 || compartment is null)
            {
                return null;
            }

            hr = compartment.GetValue(out var value);
            if (hr < 0)
            {
                return null;
            }

            return value switch
            {
                int int32 => int32,
                uint uint32 => unchecked((int)uint32),
                short int16 => int16,
                ushort uint16 => uint16,
                byte uint8 => uint8,
                sbyte int8 => int8,
                _ => null
            };
        }
        finally
        {
            if (compartment is not null && Marshal.IsComObject(compartment))
            {
                _ = Marshal.FinalReleaseComObject(compartment);
            }
        }
    }

    private static TsfCompartmentSnapshot Failure(uint clientId, string error) =>
        new(
            false,
            clientId,
            null,
            null,
            "FlowIME.Probe current thread manager only (not the target application's thread manager)",
            error);
}
