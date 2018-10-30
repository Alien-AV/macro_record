using System;
using System.Runtime.InteropServices;
using InjectAndCaptureDllEnums;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models
{
    class CaptureEngine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CaptureEventCallback(IntPtr evtBufPtr, int bufSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StatusCallback(InjectAndCaptureDllEnums.StatusCode statusCode);
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Init(CaptureEventCallback eventCb, StatusCallback statusCb);
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_start_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartCapture();
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_stop_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopCapture();

        public CaptureEngine()
        {
            _statusCallbackDelegate = StatusCb;
            _captureEventCallbackDelegate = CaptureEventCb;

            InjectAndCaptureDll.Init(_captureEventCallbackDelegate, _statusCallbackDelegate);
        }

        // if turned to a local variable, those delegates will be cleaned up and callbacks from the DLL will fail
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly InjectAndCaptureDll.CaptureEventCallback _captureEventCallbackDelegate;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly InjectAndCaptureDll.StatusCallback _statusCallbackDelegate;

        private void CaptureEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = ProtobufGenerated.InputEvent.Parser.ParseFrom(evtBuf);

            OnCapturedEvent(new CaptureEventsEventArgs(parsedEvent));
        }

        private void StatusCb(InjectAndCaptureDllEnums.StatusCode statusCode)
        {
             OnCaptureStatus(new CaptureStatusEventArgs(statusCode));
        }

        #region event handling
        public class CaptureEventsEventArgs : EventArgs
        {
            public CaptureEventsEventArgs(InputEvent inputEvent)
            {
                InputEvent = inputEvent;
            }

            public InputEvent InputEvent { get; }
        }
        public delegate void CaptureEventsEventHandler(object sender, CaptureEventsEventArgs e);
        public event CaptureEventsEventHandler CapturedEvent;
        protected virtual void OnCapturedEvent(CaptureEventsEventArgs e) => CapturedEvent?.Invoke(this, e);

        public class CaptureStatusEventArgs : EventArgs
        {
            public CaptureStatusEventArgs(StatusCode statusCode)
            {
                StatusCode = statusCode;
            }

            public StatusCode StatusCode { get; }
        }
        public delegate void CaptureStatusEventHandler(object sender, CaptureStatusEventArgs e);
        public event CaptureStatusEventHandler CaptureStatus;
        protected virtual void OnCaptureStatus(CaptureStatusEventArgs e) => CaptureStatus?.Invoke(this, e);
        #endregion
    }
}
