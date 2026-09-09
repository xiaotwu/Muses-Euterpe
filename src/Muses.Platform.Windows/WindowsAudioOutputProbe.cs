using System.Runtime.InteropServices;
using Muses.Core.Advanced;

namespace Muses.Platform.Windows;

/// <summary>
/// WASAPI default render device + period. Returns nulls when COM is unavailable — never invents names.
/// </summary>
public sealed class WindowsAudioOutputProbe : IAudioOutputProbe
{
    public static IAudioOutputProbe Create() =>
        OperatingSystem.IsWindows() ? new WindowsAudioOutputProbe() : NullAudioOutputProbe.Instance;

    private WindowsAudioOutputProbe()
    {
        try
        {
            Probe();
        }
        catch
        {
            DefaultDeviceName = null;
            LatencyMilliseconds = null;
        }
    }

    public string? DefaultDeviceName { get; private set; }
    public double? LatencyMilliseconds { get; private set; }

    private void Probe()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
        if (device is null) return;

        device.OpenPropertyStore(0, out var store);
        if (store is not null)
        {
            var key = PKeyDeviceFriendlyName;
            store.GetValue(ref key, out var pv);
            if (pv.vt == 31 /*VT_LPWSTR*/ && pv.pwszVal != IntPtr.Zero)
            {
                var name = Marshal.PtrToStringUni(pv.pwszVal);
                if (!string.IsNullOrWhiteSpace(name))
                    DefaultDeviceName = name.Trim();
            }
            PropVariantClear(ref pv);
        }

        // Device period needs a correctly ordered IAudioClient vtable; skip rather than crash.
        LatencyMilliseconds = null;
    }

    private static readonly PropertyKey PKeyDeviceFriendlyName = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    private enum EDataFlow { eRender = 0 }
    private enum ERole { eMultimedia = 1 }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int stgmAccess, out IPropertyStore store);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int i, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant pv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid f, int p) { fmtid = f; pid = p; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1, wReserved2, wReserved3;
        public IntPtr pwszVal;
        public IntPtr pad1, pad2;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}
