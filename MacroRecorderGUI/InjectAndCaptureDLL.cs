using System;
using System.Runtime.InteropServices;

namespace MacroRecorderGUI
{
    class InjectAndCaptureDll
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ErrorCallback(string error);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_init_with_error_cb", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitWithErrorCb(ErrorCallback cb);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Init();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CaptureEventCallback(IntPtr evtBufPtr, int bufSize);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_start_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartCapture(CaptureEventCallback cb);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_stop_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopCapture();

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_event", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InjectEvent(IntPtr cppBuffer, int sizeOfCppBuffer);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_events", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InjectEvents(IntPtr cppBuffer, int sizeOfCppBuffer);
    }
}
