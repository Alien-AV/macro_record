using System;
using System.Runtime.InteropServices;

namespace MacroRecorderGUI
{
    class InjectAndCaptureDll
    {
        public delegate void IacDllCaptureEventCb(IntPtr evtBufPtr, int bufSize);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iac_dll_init();

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_start_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iac_dll_start_capture(IacDllCaptureEventCb cb);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_stop_capture", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iac_dll_stop_capture();

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_event", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iac_dll_inject_event(IntPtr cppBuffer, int sizeOfCppBuffer);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_events", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iac_dll_inject_events(IntPtr cppBuffer, int sizeOfCppBuffer);
    }
}
