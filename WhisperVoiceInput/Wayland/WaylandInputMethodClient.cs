using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Wayland;

public sealed class WaylandInputMethodClient : IWaylandInputMethodClient
{
    private readonly ILogger _logger;
    private readonly object _lock = new();

    // State
    private volatile bool _isAvailable;
    private volatile bool _isActive;
    private volatile bool _disposed;
    private uint _serial;

    // Pending commit request
    private string? _pendingText;
    private TaskCompletionSource<bool>? _pendingTcs;

    // Background thread
    private Thread? _eventLoopThread;
    private CancellationTokenSource? _cts;

    // Native resources (only accessed from the event loop thread)
    private WaylandLib? _lib;
    private IntPtr _display;
    private IntPtr _registry;
    private IntPtr _seatProxy;
    private IntPtr _managerProxy;
    private IntPtr _imProxy;

    // Protocol interface definitions (unmanaged memory, allocated once per Start)
    private IntPtr _managerIface;
    private IntPtr _imIface;

    // GCHandles for pinned listener arrays
    private GCHandle _regListenerHandle;
    private GCHandle _imListenerHandle;

    // Delegate instances kept alive to prevent GC collection
    private GlobalCallback? _globalCb;
    private GlobalRemoveCallback? _globalRemoveCb;
    private NoArgsCallback? _activateCb;
    private NoArgsCallback? _deactivateCb;
    private SurroundingTextCallback? _surroundingTextCb;
    private UintCallback? _textChangeCauseCb;
    private TwoUintCallback? _contentTypeCb;
    private NoArgsCallback? _doneCb;
    private NoArgsCallback? _unavailableCb;

    public bool IsAvailable => _isAvailable;
    public bool IsActive => _isActive;

    public WaylandInputMethodClient(ILogger logger)
    {
        _logger = logger.ForContext<WaylandInputMethodClient>();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                _logger.Warning("Cannot start disposed WaylandInputMethodClient");
                return;
            }

            if (_cts != null)
            {
                _logger.Debug("Wayland IME client already running, ignoring Start()");
                return;
            }

            _logger.Information("Starting Wayland IME client");
            _cts = new CancellationTokenSource();
            var cts = _cts;

            _eventLoopThread = new Thread(() => EventLoop(cts.Token))
            {
                Name = "WaylandIME",
                IsBackground = true
            };
            _eventLoopThread.Start();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Thread? thread;

        lock (_lock)
        {
            if (_cts == null)
            {
                _logger.Debug("Wayland IME client not running, ignoring Stop()");
                return;
            }

            _logger.Information("Stopping Wayland IME client");
            cts = _cts;
            thread = _eventLoopThread;
            _cts = null;
            _eventLoopThread = null;
        }

        cts.Cancel();
        thread?.Join(TimeSpan.FromSeconds(3));
        cts.Dispose();

        // Fail any pending request
        FailPending("Client stopped");
    }

    public Task<bool> CommitTextAsync(string text)
    {
        if (!_isAvailable)
        {
            _logger.Warning("Wayland IME not available, cannot commit text");
            return Task.FromResult(false);
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            // If there's already a pending request, fail it
            _pendingTcs?.TrySetResult(false);

            _pendingText = text;
            _pendingTcs = tcs;
        }

        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private unsafe void EventLoop(CancellationToken ct)
    {
        try
        {
            if (!TryConnect())
                return;

            _logger.Information("Wayland IME event loop started");

            while (!ct.IsCancellationRequested)
            {
                var ret = _lib!.DisplayRoundtrip(_display);
                if (ret < 0)
                {
                    var err = _lib.DisplayGetError(_display);
                    _logger.Error("Wayland display roundtrip error: errno={Errno}", err);
                    break;
                }

                TryProcessPendingCommit();

                try { ct.WaitHandle.WaitOne(100); }
                catch (ObjectDisposedException) { break; }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Wayland IME event loop failed unexpectedly");
        }
        finally
        {
            _isAvailable = false;
            _isActive = false;
            CleanupNativeResources();
            _logger.Debug("Wayland IME client cleaned up");
        }
    }

    private unsafe bool TryConnect()
    {
        // Step 1: Check WAYLAND_DISPLAY
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (string.IsNullOrEmpty(waylandDisplay))
        {
            _logger.Information("WAYLAND_DISPLAY not set, Wayland IME output unavailable");
            return false;
        }

        // Step 2: Load libwayland-client
        _lib = WaylandLib.TryLoad();
        if (_lib == null)
        {
            _logger.Information("{LibName} not found, Wayland IME output unavailable", WaylandLib.LibName);
            return false;
        }

        // Step 3: Connect to Wayland display
        _display = _lib.DisplayConnect(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            _logger.Warning("Failed to connect to Wayland display");
            return false;
        }

        // Step 4: Get registry
        var regArgs = stackalloc WlArgument[1];
        regArgs[0].n = 0;
        _registry = _lib.ProxyMarshalArrayConstructorVersioned(
            _display, 1, regArgs, _lib.WlRegistryInterfacePtr, 1);
        if (_registry == IntPtr.Zero)
        {
            _logger.Warning("Failed to get wl_registry");
            return false;
        }

        // Step 5: Enumerate globals to find required protocols
        uint seatName = 0, managerName = 0;
        bool foundSeat = false, foundManager = false;

        _globalCb = new GlobalCallback((_, _, name, ifacePtr, ver) =>
        {
            var iface = Marshal.PtrToStringUTF8(ifacePtr) ?? "";
            if (iface == "wl_seat" && !foundSeat)
            {
                seatName = name;
                foundSeat = true;
            }
            if (iface == "zwp_input_method_manager_v2" && !foundManager)
            {
                managerName = name;
                foundManager = true;
            }
        });
        _globalRemoveCb = new GlobalRemoveCallback((_, _, _) => { });

        var regListener = new IntPtr[]
        {
            Marshal.GetFunctionPointerForDelegate(_globalCb),
            Marshal.GetFunctionPointerForDelegate(_globalRemoveCb)
        };
        _regListenerHandle = GCHandle.Alloc(regListener, GCHandleType.Pinned);
        _lib.ProxyAddListener(_registry, _regListenerHandle.AddrOfPinnedObject(), IntPtr.Zero);
        _lib.DisplayRoundtrip(_display);

        if (!foundManager)
        {
            _logger.Information("zwp_input_method_manager_v2 not found in compositor globals");
            return false;
        }
        if (!foundSeat)
        {
            _logger.Information("wl_seat not found in compositor globals");
            return false;
        }

        // Step 6: Allocate protocol interface definitions
        var managerMethods = WlMem.AllocMessages(
            ("get_input_method", "on"),
            ("destroy", "")
        );
        _managerIface = WlMem.AllocInterface(
            "zwp_input_method_manager_v2", 1,
            managerMethods, 2, IntPtr.Zero, 0);

        var imMethods = WlMem.AllocMessages(
            ("commit_string", "s"),
            ("set_preedit_string", "sii"),
            ("delete_surrounding_text", "uu"),
            ("commit", "u"),
            ("get_input_popup_surface", "no"),
            ("grab_keyboard", "n"),
            ("destroy", "")
        );
        var imEvents = WlMem.AllocMessages(
            ("activate", ""),
            ("deactivate", ""),
            ("surrounding_text", "suu"),
            ("text_change_cause", "u"),
            ("content_type", "uu"),
            ("done", ""),
            ("unavailable", "")
        );
        _imIface = WlMem.AllocInterface(
            "zwp_input_method_v2", 1,
            imMethods, 7, imEvents, 7);

        // Step 7: Bind wl_seat
        var seatNamePtr = WlMem.AllocStr("wl_seat");
        var seatBindArgs = stackalloc WlArgument[4];
        seatBindArgs[0].u = seatName;
        seatBindArgs[1].s = seatNamePtr;
        seatBindArgs[2].u = 7;
        seatBindArgs[3].n = 0;
        _seatProxy = _lib.ProxyMarshalArrayConstructorVersioned(
            _registry, 0, seatBindArgs, _lib.WlSeatInterfacePtr, 7);

        // Step 8: Bind zwp_input_method_manager_v2
        var managerNamePtr = WlMem.AllocStr("zwp_input_method_manager_v2");
        var managerBindArgs = stackalloc WlArgument[4];
        managerBindArgs[0].u = managerName;
        managerBindArgs[1].s = managerNamePtr;
        managerBindArgs[2].u = 1;
        managerBindArgs[3].n = 0;
        _managerProxy = _lib.ProxyMarshalArrayConstructorVersioned(
            _registry, 0, managerBindArgs, _managerIface, 1);

        _lib.DisplayRoundtrip(_display);

        if (_seatProxy == IntPtr.Zero)
        {
            _logger.Warning("Failed to bind wl_seat");
            return false;
        }
        if (_managerProxy == IntPtr.Zero)
        {
            _logger.Warning("Failed to bind zwp_input_method_manager_v2");
            return false;
        }

        // Step 9: Create zwp_input_method_v2
        var imGetArgs = stackalloc WlArgument[2];
        imGetArgs[0].o = _seatProxy;
        imGetArgs[1].n = 0;
        _imProxy = _lib.ProxyMarshalArrayConstructorVersioned(
            _managerProxy, 0, imGetArgs, _imIface, 1);

        if (_imProxy == IntPtr.Zero)
        {
            _logger.Warning("Failed to create zwp_input_method_v2");
            return false;
        }

        // Step 10: Add IME event listeners
        SetupImeListeners();

        _lib.DisplayFlush(_display);
        _isAvailable = true;
        _logger.Information("Wayland IME client connected and ready");
        return true;
    }

    private void SetupImeListeners()
    {
        _activateCb = new NoArgsCallback((_, _) =>
        {
            _isActive = true;
            _logger.Debug("Wayland IME: text field activated");
        });
        _deactivateCb = new NoArgsCallback((_, _) =>
        {
            _isActive = false;
            _logger.Debug("Wayland IME: text field deactivated");
        });
        _surroundingTextCb = new SurroundingTextCallback((_, _, _, _, _) => { });
        _textChangeCauseCb = new UintCallback((_, _, _) => { });
        _contentTypeCb = new TwoUintCallback((_, _, _, _) => { });
        _doneCb = new NoArgsCallback((_, _) =>
        {
            _serial++;
            _logger.Debug("Wayland IME: done event, serial={Serial}, isActive={IsActive}", _serial, _isActive);
        });
        _unavailableCb = new NoArgsCallback((_, _) =>
        {
            _isAvailable = false;
            _logger.Warning("Wayland IME unavailable -- another IME (fcitx/ibus) is likely active");
        });

        // Event order: activate(0), deactivate(1), surrounding_text(2),
        //              text_change_cause(3), content_type(4), done(5), unavailable(6)
        var imListener = new IntPtr[]
        {
            Marshal.GetFunctionPointerForDelegate(_activateCb),
            Marshal.GetFunctionPointerForDelegate(_deactivateCb),
            Marshal.GetFunctionPointerForDelegate(_surroundingTextCb),
            Marshal.GetFunctionPointerForDelegate(_textChangeCauseCb),
            Marshal.GetFunctionPointerForDelegate(_contentTypeCb),
            Marshal.GetFunctionPointerForDelegate(_doneCb),
            Marshal.GetFunctionPointerForDelegate(_unavailableCb),
        };
        _imListenerHandle = GCHandle.Alloc(imListener, GCHandleType.Pinned);
        _lib!.ProxyAddListener(_imProxy, _imListenerHandle.AddrOfPinnedObject(), IntPtr.Zero);
    }

    private unsafe void TryProcessPendingCommit()
    {
        string? text;
        TaskCompletionSource<bool>? tcs;

        lock (_lock)
        {
            text = _pendingText;
            tcs = _pendingTcs;
            if (text == null || tcs == null) return;
            _pendingText = null;
            _pendingTcs = null;
        }

        if (!_isActive || !_isAvailable)
        {
            _logger.Debug("Cannot commit: isActive={IsActive}, isAvailable={IsAvailable}", _isActive, _isAvailable);
            tcs.TrySetResult(false);
            return;
        }

        try
        {
            // commit_string(text) — opcode 0
            var utf8 = Encoding.UTF8.GetBytes(text + "\0");
            fixed (byte* p = utf8)
            {
                var a = stackalloc WlArgument[1];
                a[0].s = (IntPtr)p;
                _lib!.ProxyMarshalArray(_imProxy, 0, a);
            }

            // commit(serial) — opcode 3
            var b = stackalloc WlArgument[1];
            b[0].u = _serial;
            _lib!.ProxyMarshalArray(_imProxy, 3, b);

            _lib.DisplayFlush(_display);

            _logger.Information("Text committed via Wayland IME ({Length} chars)", text.Length);
            tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to commit text via Wayland IME");
            tcs.TrySetResult(false);
        }
    }

    private void FailPending(string reason)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_lock)
        {
            tcs = _pendingTcs;
            _pendingTcs = null;
            _pendingText = null;
        }
        if (tcs != null)
        {
            _logger.Debug("Failing pending commit: {Reason}", reason);
            tcs.TrySetResult(false);
        }
    }

    private void CleanupNativeResources()
    {
        try
        {
            // Destroy proxies in reverse creation order
            if (_lib != null)
            {
                if (_imProxy != IntPtr.Zero) { _lib.ProxyDestroy(_imProxy); _imProxy = IntPtr.Zero; }
                if (_managerProxy != IntPtr.Zero) { _lib.ProxyDestroy(_managerProxy); _managerProxy = IntPtr.Zero; }
                if (_seatProxy != IntPtr.Zero) { _lib.ProxyDestroy(_seatProxy); _seatProxy = IntPtr.Zero; }
                if (_registry != IntPtr.Zero) { _lib.ProxyDestroy(_registry); _registry = IntPtr.Zero; }
                if (_display != IntPtr.Zero) { _lib.DisplayDisconnect(_display); _display = IntPtr.Zero; }
            }

            // Free GCHandles
            if (_imListenerHandle.IsAllocated) _imListenerHandle.Free();
            if (_regListenerHandle.IsAllocated) _regListenerHandle.Free();

            // Free native library
            _lib?.Dispose();
            _lib = null;

            // Clear delegate references
            _globalCb = null;
            _globalRemoveCb = null;
            _activateCb = null;
            _deactivateCb = null;
            _surroundingTextCb = null;
            _textChangeCauseCb = null;
            _contentTypeCb = null;
            _doneCb = null;
            _unavailableCb = null;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error during Wayland IME native resource cleanup");
        }
    }
}
