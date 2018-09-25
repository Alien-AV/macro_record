using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacroRecorderGUI
{
    class InjectAndCaptureDll
    {
        public delegate void IacDllCaptureEventCb(IntPtr evtBufPtr, int bufSize);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_init")]
        public static extern void iac_dll_init();

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_start_capture")]
        public static extern void iac_dll_start_capture(IacDllCaptureEventCb cb);

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_stop_capture")]
        public static extern void iac_dll_stop_capture();

        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_event")]
        public static extern void iac_dll_inject_event(IntPtr cppBuffer, int sizeOfCppBuffer);


    }
}
