using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WhisperVoiceInput.Wayland;

/// <summary>wl_message — 24 bytes on 64-bit.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WlMessage
{
    public IntPtr name;      // const char*
    public IntPtr signature; // const char* — 's','u','i','o','n',...
    public IntPtr types;     // const WlInterface** (IntPtr.Zero = no obj-args)
}

/// <summary>wl_interface — 40 bytes on 64-bit (with padding at offset 28).</summary>
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct WlInterface
{
    [FieldOffset(0)]  public IntPtr name;
    [FieldOffset(8)]  public int    version;
    [FieldOffset(12)] public int    method_count;
    [FieldOffset(16)] public IntPtr methods;      // WlMessage*
    [FieldOffset(24)] public int    event_count;
    // offset 28: 4-byte implicit padding
    [FieldOffset(32)] public IntPtr events;       // WlMessage*
}

/// <summary>wl_argument union — 8 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct WlArgument
{
    [FieldOffset(0)] public int    i;
    [FieldOffset(0)] public uint   u;
    [FieldOffset(0)] public IntPtr s; // const char*
    [FieldOffset(0)] public IntPtr o; // wl_object* (proxy)
    [FieldOffset(0)] public uint   n; // new_id
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void NoArgsCallback(IntPtr data, IntPtr proxy);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void GlobalCallback(IntPtr data, IntPtr registry, uint name, IntPtr iface, uint version);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void GlobalRemoveCallback(IntPtr data, IntPtr registry, uint name);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SurroundingTextCallback(IntPtr data, IntPtr proxy, IntPtr text, uint cursor, uint anchor);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void UintCallback(IntPtr data, IntPtr proxy, uint value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void TwoUintCallback(IntPtr data, IntPtr proxy, uint a, uint b);

/// <summary>
/// Dynamically loaded libwayland-client function pointers.
/// All functions are loaded via NativeLibrary to allow graceful absence detection.
/// </summary>
internal sealed class WaylandLib : IDisposable
{
    public const string LibName = "libwayland-client.so.0";

    private IntPtr _handle;

    // Function pointer delegates for each libwayland-client function we use
    public unsafe delegate IntPtr DisplayConnectDelegate(IntPtr name);
    public delegate void DisplayDisconnectDelegate(IntPtr display);
    public delegate int DisplayRoundtripDelegate(IntPtr display);
    public delegate int DisplayDispatchDelegate(IntPtr display);
    public delegate int DisplayFlushDelegate(IntPtr display);
    public delegate int DisplayGetErrorDelegate(IntPtr display);
    public unsafe delegate IntPtr ProxyMarshalArrayConstructorVersionedDelegate(
        IntPtr proxy, uint opcode, WlArgument* args, IntPtr iface, uint version);
    public unsafe delegate void ProxyMarshalArrayDelegate(
        IntPtr proxy, uint opcode, WlArgument* args);
    public delegate int ProxyAddListenerDelegate(
        IntPtr proxy, IntPtr implementation, IntPtr data);
    public delegate void ProxyDestroyDelegate(IntPtr proxy);

    // Loaded function pointers
    public DisplayConnectDelegate DisplayConnect { get; }
    public DisplayDisconnectDelegate DisplayDisconnect { get; }
    public DisplayRoundtripDelegate DisplayRoundtrip { get; }
    public DisplayDispatchDelegate DisplayDispatch { get; }
    public DisplayFlushDelegate DisplayFlush { get; }
    public DisplayGetErrorDelegate DisplayGetError { get; }
    public ProxyMarshalArrayConstructorVersionedDelegate ProxyMarshalArrayConstructorVersioned { get; }
    public ProxyMarshalArrayDelegate ProxyMarshalArray { get; }
    public ProxyAddListenerDelegate ProxyAddListener { get; }
    public ProxyDestroyDelegate ProxyDestroy { get; }

    // Data symbol exports
    public IntPtr WlRegistryInterfacePtr { get; }
    public IntPtr WlSeatInterfacePtr { get; }

    private WaylandLib(IntPtr handle)
    {
        _handle = handle;

        DisplayConnect = Marshal.GetDelegateForFunctionPointer<DisplayConnectDelegate>(
            NativeLibrary.GetExport(handle, "wl_display_connect"));
        DisplayDisconnect = Marshal.GetDelegateForFunctionPointer<DisplayDisconnectDelegate>(
            NativeLibrary.GetExport(handle, "wl_display_disconnect"));
        DisplayRoundtrip = Marshal.GetDelegateForFunctionPointer<DisplayRoundtripDelegate>(
            NativeLibrary.GetExport(handle, "wl_display_roundtrip"));
        DisplayDispatch = Marshal.GetDelegateForFunctionPointer<DisplayDispatchDelegate>(
            NativeLibrary.GetExport(handle, "wl_display_dispatch"));
        DisplayFlush = Marshal.GetDelegateForFunctionPointer<DisplayFlushDelegate>(
            NativeLibrary.GetExport(handle, "wl_display_flush"));
        DisplayGetError = Marshal.GetDelegateForFunctionPointer<DisplayGetErrorDelegate>(
            NativeLibrary.GetExport(handle, "wl_display_get_error"));
        ProxyMarshalArrayConstructorVersioned = Marshal.GetDelegateForFunctionPointer<ProxyMarshalArrayConstructorVersionedDelegate>(
            NativeLibrary.GetExport(handle, "wl_proxy_marshal_array_constructor_versioned"));
        ProxyMarshalArray = Marshal.GetDelegateForFunctionPointer<ProxyMarshalArrayDelegate>(
            NativeLibrary.GetExport(handle, "wl_proxy_marshal_array"));
        ProxyAddListener = Marshal.GetDelegateForFunctionPointer<ProxyAddListenerDelegate>(
            NativeLibrary.GetExport(handle, "wl_proxy_add_listener"));
        ProxyDestroy = Marshal.GetDelegateForFunctionPointer<ProxyDestroyDelegate>(
            NativeLibrary.GetExport(handle, "wl_proxy_destroy"));

        WlRegistryInterfacePtr = NativeLibrary.GetExport(handle, "wl_registry_interface");
        WlSeatInterfacePtr = NativeLibrary.GetExport(handle, "wl_seat_interface");
    }

    /// <summary>
    /// Try to load libwayland-client. Returns null if the library is not available.
    /// </summary>
    public static WaylandLib? TryLoad()
    {
        if (!NativeLibrary.TryLoad(LibName, out var handle))
            return null;
        return new WaylandLib(handle);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeLibrary.Free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}

/// <summary>
/// Helper for allocating Wayland protocol structures in unmanaged memory.
/// </summary>
internal static unsafe class WlMem
{
    public static IntPtr AllocStr(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr + bytes.Length, 0);
        return ptr;
    }

    public static IntPtr AllocMessages(params (string name, string sig)[] msgs)
    {
        if (msgs.Length == 0) return IntPtr.Zero;
        var ptr = Marshal.AllocHGlobal(sizeof(WlMessage) * msgs.Length);
        var arr = (WlMessage*)ptr;
        for (int idx = 0; idx < msgs.Length; idx++)
        {
            arr[idx] = new WlMessage
            {
                name      = AllocStr(msgs[idx].name),
                signature = AllocStr(msgs[idx].sig),
                types     = IntPtr.Zero
            };
        }
        return ptr;
    }

    public static IntPtr AllocInterface(string name, int version,
        IntPtr methods, int methodCount, IntPtr events, int eventCount)
    {
        var ptr = Marshal.AllocHGlobal(sizeof(WlInterface));
        var iface = (WlInterface*)ptr;
        *iface = new WlInterface
        {
            name         = AllocStr(name),
            version      = version,
            method_count = methodCount,
            methods      = methods,
            event_count  = eventCount,
            events       = events
        };
        return ptr;
    }
}
