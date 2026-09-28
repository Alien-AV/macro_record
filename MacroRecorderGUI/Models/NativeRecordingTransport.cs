using System.Diagnostics;
using System.Runtime.InteropServices;
using Google.Protobuf;
using ProtobufGenerated;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUI.Models;

internal sealed class NativeRecordingTransport : IRecordingTransport
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void InputCallback(nint buffer, int bufferSize, ulong sessionId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void BoundaryCallback(ulong sessionId, RecordingBoundary boundary, RecordingStartKeys heldKeys, RecordingStartKeys idleReleasedKeys, int originX, int originY, uint originValid);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StatusCallback(StatusCode status);

    internal interface INativeApi
    {
        // Rejected initialization retains no callbacks owned by this caller.
        bool Initialize(InputCallback input, StatusCallback status, BoundaryCallback boundary);
        bool Start(ulong sessionId, RecordingStopGestures stopGestures, uint[] captureGestures);
        bool Stop(ulong sessionId, RecordingStopGestures gesture, uint messageTime);
        bool CapturedWait(ulong sessionId, uint modifiers, uint key, uint messageTime, byte[] condition);
        void Shutdown(); // A normal return proves both native threads joined.
    }

    private readonly INativeApi _native;
    private readonly InputCallback _inputCallback;
    private readonly BoundaryCallback _boundaryCallback;
    private readonly StatusCallback _statusCallback;
    private GCHandle _callbackRoot;
    private bool _disposed;
    private bool _joined;
    private ulong _failedSession;

    public NativeRecordingTransport() : this(new NativeApi()) { }

    internal NativeRecordingTransport(INativeApi native)
    {
        _native = native;
        _inputCallback = OnInput;
        _boundaryCallback = OnBoundary;
        _statusCallback = OnStatus;
        // Unmanaged function pointers do not root delegates. Retain this owner
        // even if a caller abandons it after a failed shutdown; only a join releases it.
        _callbackRoot = GCHandle.Alloc(this);
        try
        {
            if (!_native.Initialize(_inputCallback, _statusCallback, _boundaryCallback))
                throw new InvalidOperationException("The native capture thread could not initialize.");
        }
        catch
        {
            _callbackRoot.Free();
            throw;
        }
    }

    public event Action<ulong, ProtobufInputEvent>? Input;
    public event Action<ulong, RecordingBoundary, RecordingStartKeys, RecordingStartKeys, PointerPosition?>? Boundary;
    public event Action<StatusCode>? Status;

    public void Start(ulong sessionId, RecordingStopGestures stopGestures, IReadOnlyList<RecordingCaptureGesture>? captureGestures = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var captures = (captureGestures ?? []).SelectMany(gesture => new[] { gesture.Modifiers, gesture.VirtualKey }).ToArray();
        if (!_native.Start(sessionId, stopGestures, captures)) throw new InvalidOperationException("The capture thread could not start recording (check capture shortcut configuration).");
    }

    public bool CapturedWait(ulong sessionId, WaitCondition? condition, RecordingCaptureGesture gesture, uint messageTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _native.CapturedWait(sessionId, gesture.Modifiers, gesture.VirtualKey, messageTime, condition?.ToByteArray() ?? []);
    }

    public void Stop(ulong sessionId, RecordingStopCommand? command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_native.Stop(sessionId, command?.Gesture ?? RecordingStopGestures.None, command?.MessageTime ?? 0))
            throw new InvalidOperationException("The capture thread could not stop recording.");
    }

    private void OnInput(nint buffer, int bufferSize, ulong sessionId)
    {
        if (sessionId == _failedSession) return;
        try
        {
            // No single input event can exceed the size of a supported macro.
            // Check before allocating or touching the borrowed native range.
            if (buffer == 0 || bufferSize <= 0 || bufferSize > RecordingLibraryStore.MaximumMacroBytes)
                throw new InvalidDataException("The native capture callback supplied an invalid buffer.");
            var bytes = new byte[bufferSize];
            Marshal.Copy(buffer, bytes, 0, bufferSize);
            var input = ProtobufInputEvent.Parser.ParseFrom(bytes);
            Input?.Invoke(sessionId, input);
        }
        catch (Exception error)
        {
            RequestFailedStop(sessionId, error);
        }
    }

    private void OnBoundary(ulong sessionId, RecordingBoundary boundary, RecordingStartKeys heldKeys,
        RecordingStartKeys idleReleasedKeys, int x, int y, uint valid)
    {
        // Input/boundary delivery is serialized on the native collector. A failed
        // callback requests a stop, but cannot complete a session before its real end.
        var terminal = boundary is RecordingBoundary.Stopped or RecordingBoundary.Failed;
        if (terminal && sessionId == _failedSession) boundary = RecordingBoundary.Failed;
        foreach (var handler in Delegate.EnumerateInvocationList(Boundary))
        {
            try { handler(sessionId, boundary, heldKeys, idleReleasedKeys, valid == 1 ? new PointerPosition(x, y) : null); }
            catch (Exception error)
            {
                if (!terminal) RequestFailedStop(sessionId, error);
                else
                {
                    Trace.TraceError("Recording completion callback failed: {0}", error);
                    OnStatus(StatusCode.ErrorCouldNotProcessInputData);
                    boundary = RecordingBoundary.Failed;
                }
            }
        }
    }

    private void RequestFailedStop(ulong sessionId, Exception error)
    {
        if (sessionId == _failedSession) return;
        _failedSession = sessionId;
        Trace.TraceError("Recording callback failed for session {0}: {1}", sessionId, error);
        try
        {
            // Stop only posts a command. Shutdown here would join our own collector.
            if (!_native.Stop(sessionId, RecordingStopGestures.None, 0))
                Trace.TraceError("The native capture thread rejected the stop for failed session {0}.", sessionId);
        }
        catch (Exception stopError) { Trace.TraceError("Could not request capture stop: {0}", stopError); }
        OnStatus(StatusCode.ErrorCouldNotProcessInputData);
    }

    private void OnStatus(StatusCode status)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(Status))
        {
            try { handler(status); }
            catch (Exception error) { Trace.TraceError("Recording status callback failed: {0}", error); }
        }
    }

    public void Dispose()
    {
        if (_joined) return;
        _disposed = true;
        _native.Shutdown();
        _joined = true;
        _callbackRoot.Free();
    }

    private sealed class NativeApi : INativeApi
    {
        public bool Initialize(InputCallback input, StatusCallback status, BoundaryCallback boundary) => DllInit(input, status, boundary);
        public bool Start(ulong sessionId, RecordingStopGestures stopGestures, uint[] captureGestures) => DllStartRecord(sessionId, stopGestures, captureGestures, (uint)captureGestures.Length / 2);
        public bool Stop(ulong sessionId, RecordingStopGestures gesture, uint messageTime) => DllStopRecord(sessionId, gesture, messageTime);
        public bool CapturedWait(ulong sessionId, uint modifiers, uint key, uint messageTime, byte[] condition) => DllCapturedWait(sessionId, modifiers, key, messageTime, condition, condition.Length);
        public void Shutdown() => DllShutdown();

        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_init_v2", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool DllInit(InputCallback input, StatusCallback status, BoundaryCallback boundary);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_start_record", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool DllStartRecord(ulong sessionId, RecordingStopGestures stopGestures, uint[] captureGestures, uint captureCount);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_record_captured_wait", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool DllCapturedWait(ulong sessionId, uint modifiers, uint key, uint messageTime, byte[] condition, int size);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_stop_record", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool DllStopRecord(ulong sessionId, RecordingStopGestures gesture, uint messageTime);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_record_shutdown", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DllShutdown();
    }
}
