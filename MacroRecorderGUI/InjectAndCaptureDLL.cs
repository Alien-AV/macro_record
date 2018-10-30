using System;
using System.Runtime.InteropServices;

namespace MacroRecorderGUI
{
    class InjectAndCaptureDll
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CaptureEventCallback(IntPtr evtBufPtr, int bufSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StatusCallback(InjectAndCaptureDllEnums.StatusCode statusCode);

        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Init(CaptureEventCallback eventCb, StatusCallback statusCb);
        
        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_start_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartCapture();

        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_stop_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopCapture();

        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events_abort", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InjectEventAbort();

        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_event", CallingConvention = CallingConvention.Cdecl)]
        private static extern void InjectEventCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);

        [System.Runtime.InteropServices.DllImportAttribute("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_events", CallingConvention = CallingConvention.Cdecl)]
        private static extern void InjectEventsCppBuffer(IntPtr cppBuffer, int sizeOfCppBuffer);

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
    }
}
