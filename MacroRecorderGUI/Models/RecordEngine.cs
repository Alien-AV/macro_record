using System;
using System.Runtime.InteropServices;
using RecordPlaybackDLLEnums;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models
{
    class RecordEngine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void RecordEventCallback(IntPtr evtBufPtr, int bufSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StatusCallback(RecordPlaybackDLLEnums.StatusCode statusCode);
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Init(RecordEventCallback eventCb, StatusCallback statusCb);
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_start_record", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartRecord();
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_stop_record", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopRecord();

        public RecordEngine()
        {
            _statusCallbackDelegate = StatusCb;
            _recordEventCallbackDelegate = RecordEventCb;

            Init(_recordEventCallbackDelegate, _statusCallbackDelegate);
        }

        // if turned to a local variable, those delegates will be cleaned up and callbacks from the DLL will fail
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly RecordEventCallback _recordEventCallbackDelegate;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly StatusCallback _statusCallbackDelegate;

        private void RecordEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = ProtobufGenerated.ProtobufInputEvent.Parser.ParseFrom(evtBuf);

            OnRecordedEvent(new RecordEventsEventArgs(parsedEvent));
        }

        private void StatusCb(RecordPlaybackDLLEnums.StatusCode statusCode)
        {
             OnRecordStatus(new RecordStatusEventArgs(statusCode));
        }

        #region event handling
        public class RecordEventsEventArgs : EventArgs
        {
            public RecordEventsEventArgs(ProtobufInputEvent inputEvent)
            {
                InputEvent = inputEvent;
            }

            public ProtobufInputEvent InputEvent { get; }
        }
        public delegate void RecordEventsEventHandler(object sender, RecordEventsEventArgs e);
        public event RecordEventsEventHandler RecordedEvent;
        protected virtual void OnRecordedEvent(RecordEventsEventArgs e) => RecordedEvent?.Invoke(this, e);

        public class RecordStatusEventArgs : EventArgs
        {
            public RecordStatusEventArgs(StatusCode statusCode)
            {
                StatusCode = statusCode;
            }

            public StatusCode StatusCode { get; }
        }
        public delegate void RecordStatusEventHandler(object sender, RecordStatusEventArgs e);
        public event RecordStatusEventHandler RecordStatus;
        protected virtual void OnRecordStatus(RecordStatusEventArgs e) => RecordStatus?.Invoke(this, e);
        #endregion
    }
}
