using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacroRecorderGUI
{
    class InjectAndCaptureDLL
    {

        /// Return Type: void
        ///param0: char[]
        public delegate void iac_dll_capture_event_cb(string param0);

        /// Return Type: void
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_init")]
        public static extern void iac_dll_init();


        /// Return Type: void
        ///cb: iac_dll_capture_event_cb
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_start_capture")]
        public static extern void iac_dll_start_capture(iac_dll_capture_event_cb cb);


        /// Return Type: void
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_stop_capture")]
        public static extern void iac_dll_stop_capture();


        /// Return Type: void
        ///param0: char[]
        [System.Runtime.InteropServices.DllImportAttribute("InjectAndCaptureDll.dll", EntryPoint = "iac_dll_inject_event")]
        public static extern void iac_dll_inject_event(string param0);


    }
}
