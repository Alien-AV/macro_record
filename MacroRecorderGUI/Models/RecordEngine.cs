using System.Runtime.InteropServices;
using RecordPlaybackDLLEnums;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

public interface IRecordEngine
{
    event RecordEngine.RecordEventsEventHandler? RecordedEvent;
    event RecordEngine.RecordStatusEventHandler? RecordStatus;
    void StartRecord();
    void StopRecord();
}

public sealed class RecordEngine : IRecordEngine
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RecordEventCallback(nint eventBuffer, int bufferSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void StatusCallback(StatusCode statusCode);

    public delegate void RecordEventsEventHandler(object? sender, RecordEventsEventArgs e);
    public delegate void RecordStatusEventHandler(object? sender, RecordStatusEventArgs e);

    private readonly RecordEventCallback _recordEventCallback;
    private readonly StatusCallback _statusCallback;

    public RecordEngine()
    {
        _recordEventCallback = RecordEventCallbackHandler;
        _statusCallback = StatusCallbackHandler;
        DllInit(_recordEventCallback, _statusCallback);
    }

    public event RecordEventsEventHandler? RecordedEvent;
    public event RecordStatusEventHandler? RecordStatus;

    public void StartRecord()
    {
        DllStartRecord();
    }

    public void StopRecord()
    {
        DllStopRecord();
    }

    private void RecordEventCallbackHandler(nint eventBuffer, int bufferSize)
    {
        var serializedEvent = new byte[bufferSize];
        Marshal.Copy(eventBuffer, serializedEvent, 0, bufferSize);
        var parsedEvent = ProtobufInputEvent.Parser.ParseFrom(serializedEvent);
        RecordedEvent?.Invoke(this, new RecordEventsEventArgs(parsedEvent));
    }

    private void StatusCallbackHandler(StatusCode statusCode)
    {
        RecordStatus?.Invoke(this, new RecordStatusEventArgs(statusCode));
    }

    public sealed class RecordEventsEventArgs(ProtobufInputEvent inputEvent) : EventArgs
    {
        public ProtobufInputEvent InputEvent { get; } = inputEvent;
    }

    public sealed class RecordStatusEventArgs(StatusCode statusCode) : EventArgs
    {
        public StatusCode StatusCode { get; } = statusCode;
    }

    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DllInit(RecordEventCallback eventCallback, StatusCallback statusCallback);

    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_start_record", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DllStartRecord();

    [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_stop_record", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DllStopRecord();
}
