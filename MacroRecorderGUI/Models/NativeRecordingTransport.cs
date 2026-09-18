using System.Runtime.InteropServices;
using Google.Protobuf;
using ProtobufGenerated;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUI.Models;

internal sealed class NativeRecordingTransport : IRecordingTransport
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InputCallback(nint buffer, int bufferSize, ulong sessionId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BoundaryCallback(ulong sessionId, RecordingBoundary boundary, RecordingStartKeys heldKeys);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StatusCallback(StatusCode status);

    private readonly InputCallback _inputCallback;
    private readonly BoundaryCallback _boundaryCallback;
    private readonly StatusCallback _statusCallback;
    private bool _disposed;

    public NativeRecordingTransport()
    {
        _inputCallback = OnInput;
        _boundaryCallback = (id, boundary, heldKeys) => Boundary?.Invoke(id, boundary, heldKeys);
        _statusCallback = status => Status?.Invoke(status);
        if (!DllInit(_inputCallback, _statusCallback, _boundaryCallback))
            throw new InvalidOperationException("The native capture thread could not initialize.");
    }

    public event Action<ulong, ProtobufInputEvent>? Input;
    public event Action<ulong, RecordingBoundary, RecordingStartKeys>? Boundary;
    public event Action<StatusCode>? Status;

    public void Start(ulong sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!DllStartRecord(sessionId)) throw new InvalidOperationException("The capture thread could not start recording.");
    }

    public void Stop(ulong sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!DllStopRecord(sessionId)) throw new InvalidOperationException("The capture thread could not stop recording.");
    }

    private void OnInput(nint buffer, int bufferSize, ulong sessionId)
    {
        try
        {
            var serialized = new byte[bufferSize];
            Marshal.Copy(buffer, serialized, 0, bufferSize);
            Input?.Invoke(sessionId, ProtobufInputEvent.Parser.ParseFrom(serialized));
        }
        catch (InvalidProtocolBufferException)
        {
            Status?.Invoke(StatusCode.ErrorCouldNotProcessInputData);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DllShutdown(); // Joins both native threads before callback delegates can be collected.
        GC.KeepAlive(_inputCallback);
        GC.KeepAlive(_boundaryCallback);
        GC.KeepAlive(_statusCallback);
    }

    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DllInit(InputCallback input, StatusCallback status, BoundaryCallback boundary);
    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_start_record", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DllStartRecord(ulong sessionId);
    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_stop_record", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool DllStopRecord(ulong sessionId);
    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_record_shutdown", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DllShutdown();
}
