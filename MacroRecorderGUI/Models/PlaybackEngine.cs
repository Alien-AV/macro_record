using System;
using System.Runtime.InteropServices;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUI.Models
{
    class PlaybackEngine
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StatusCallback(RecordPlaybackDLLEnums.StatusCode statusCode); //TODO: separate status codes for recording and playback
        //TODO: create init function for playback engine
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events_abort", CallingConvention = CallingConvention.Cdecl)]
        public static extern void PlaybackEventAbort();
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_event", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PlaybackEventCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PlaybackEventsCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);

        public PlaybackEngine()
        {
            _statusCallbackDelegate = StatusCb;
            // InitPlaybackEngine(_statusCallbackDelegate);
        }

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly StatusCallback _statusCallbackDelegate;

        private void StatusCb(RecordPlaybackDLLEnums.StatusCode statusCode)
        {
            OnPlaybackStatus(new PlaybackStatusEventArgs(statusCode));
        }

        public static void PlaybackEvents(byte[] cSharpByteArray)
        {
            var sizeOfCppBuffer = Marshal.SizeOf(cSharpByteArray[0]) * cSharpByteArray.Length;
            var cppBuffer = Marshal.AllocHGlobal(sizeOfCppBuffer);
            try
            {
                Marshal.Copy(cSharpByteArray, 0, cppBuffer, sizeOfCppBuffer);
                PlaybackEventsCppBuffer(cppBuffer, sizeOfCppBuffer);
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
