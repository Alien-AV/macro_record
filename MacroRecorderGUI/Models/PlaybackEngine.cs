using System;
using System.Runtime.InteropServices;
using InjectAndCaptureDllEnums;

namespace MacroRecorderGUI.Models
{
    class PlaybackEngine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StatusCallback(InjectAndCaptureDllEnums.StatusCode statusCode); //TODO: separate status codes for capture and playback
        //TODO: create init function for playback engine
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_events_abort", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InjectEventAbort();
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_event", CallingConvention = CallingConvention.Cdecl)]
        private static extern void InjectEventCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_events", CallingConvention = CallingConvention.Cdecl)]
        private static extern void InjectEventsCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);

        public PlaybackEngine()
        {
            _statusCallbackDelegate = StatusCb;
            // InitInjectEngine(_statusCallbackDelegate);
        }

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly StatusCallback _statusCallbackDelegate;

        private void StatusCb(InjectAndCaptureDllEnums.StatusCode statusCode)
        {
            OnPlaybackStatus(new PlaybackStatusEventArgs(statusCode));
        }

        public static void InjectEvents(byte[] cSharpByteArray)
        {
            var sizeOfCppBuffer = Marshal.SizeOf(cSharpByteArray[0]) * cSharpByteArray.Length;
            var cppBuffer = Marshal.AllocHGlobal(sizeOfCppBuffer);
            try
            {
                Marshal.Copy(cSharpByteArray, 0, cppBuffer, sizeOfCppBuffer);
                InjectEventsCppBuffer(cppBuffer, sizeOfCppBuffer);
            }
            finally
            {
                Marshal.FreeHGlobal(cppBuffer);
            }
        }

        #region event handling
        public class PlaybackStatusEventArgs : EventArgs
        {
            public PlaybackStatusEventArgs(StatusCode statusCode)
            {
                StatusCode = statusCode;
            }

            public StatusCode StatusCode { get; }
        }
        public delegate void PlaybackStatusEventHandler(object sender, PlaybackEngine.PlaybackStatusEventArgs e);
        public event PlaybackEngine.PlaybackStatusEventHandler PlaybackStatus;
        protected virtual void OnPlaybackStatus(PlaybackEngine.PlaybackStatusEventArgs e) => PlaybackStatus?.Invoke(this, e);
        #endregion
    }
}
