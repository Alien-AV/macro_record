using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUI.Models
{
    public interface IPlaybackEngine
    {
        void PlaybackEvents(IEnumerable<InputEvent> events);
        void PlaybackEventAbort();
    }

    class PlaybackEngine : IPlaybackEngine
    {
        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        //public delegate void StatusCallback(RecordPlaybackDLLEnums.StatusCode statusCode);
        //TODO: create init function for playback engine - initializing it's own separate status callback, with separate status codes
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events_abort", CallingConvention = CallingConvention.Cdecl)]
        public static extern void DllPlaybackEventAbort();
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_event", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DllPlaybackEventCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DllPlaybackEventsCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);

        // public PlaybackEngine()
        // {
            // _statusCallbackDelegate = StatusCb;
            // InitPlaybackEngine(_statusCallbackDelegate);
        // }

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        // private readonly StatusCallback _statusCallbackDelegate;

        // private void StatusCb(RecordPlaybackDLLEnums.StatusCode statusCode)
        // {
        //     OnPlaybackStatus(new PlaybackStatusEventArgs(statusCode));
        // }

        public void PlaybackEventAbort()
        {
            DllPlaybackEventAbort();
        }

        public void PlaybackEvents(IEnumerable<InputEvent> events)
        {
            PlaybackSerializedEvents(SerializeEvents.SerializeEventsToByteArray(events));
        }

        public void PlaybackSerializedEvents(byte[] cSharpByteArray)
        {
            var sizeOfCppBuffer = Marshal.SizeOf(cSharpByteArray[0]) * cSharpByteArray.Length;
            var cppBuffer = Marshal.AllocHGlobal(sizeOfCppBuffer);
            try
            {
                Marshal.Copy(cSharpByteArray, 0, cppBuffer, sizeOfCppBuffer);
                DllPlaybackEventsCppBuffer(cppBuffer, sizeOfCppBuffer);
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
