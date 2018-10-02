using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MacroRecorderGUI
{
    public partial class MainWindow : Window //TODO: this is bad, make it a separate class, call its methods from MainWindow methods
    {
        [DllImport("User32.dll")]
        private static extern bool RegisterHotKey(
            [In] IntPtr hWnd,
            [In] int id,
            [In] uint fsModifiers,
            [In] uint vk);

        [DllImport("User32.dll")]
        private static extern bool UnregisterHotKey(
            [In] IntPtr hWnd,
            [In] int id);

        private HwndSource _source;
        private const int StartRecordHotkeyId = 9000;
        private const int StopRecordHotkeyId = 9001;
        private const int PlayBackHotkeyId = 9002;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source.AddHook(HwndHook);
            RegisterHotKey();
        }

        protected override void OnClosed(EventArgs e)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
            UnregisterHotKey();
            base.OnClosed(e);
        }

        private void RegisterHotKey()
        {
            var helper = new WindowInteropHelper(this);
            const uint VK_Q = 0x51;
            const uint VK_W = 0x57;
            const uint VK_E = 0x45;
            const uint MOD_CTRL = 0x0002;
            if(!RegisterHotKey(helper.Handle, StartRecordHotkeyId, MOD_CTRL, VK_Q))
            {
                // handle error
            }
            if(!RegisterHotKey(helper.Handle, StopRecordHotkeyId, MOD_CTRL, VK_W))
            {
                // handle error
            }
            if(!RegisterHotKey(helper.Handle, PlayBackHotkeyId, MOD_CTRL, VK_E))
            {
                // handle error
            }
        }

        private void UnregisterHotKey()
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, StartRecordHotkeyId);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            switch(msg)
            {
                case WM_HOTKEY:
                    switch(wParam.ToInt32())
                    {
                        case StartRecordHotkeyId:
                            OnStartRecordHotkeyPressed();
                            handled = true;
                            break;
                        case StopRecordHotkeyId:
                            OnStopRecordHotkeyPressed();
                            handled = true;
                            break;
                        case PlayBackHotkeyId:
                            OnPlayBackHotkeyPressed();
                            handled = true;
                            break;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private void OnStartRecordHotkeyPressed()
        {
            StartRecord_Click(null, null);      //TODO: change this, and remove from implementation from .xaml.cs
        }
        private void OnStopRecordHotkeyPressed()
        {
            StopRecord_Click(null, null);
        }
        private void OnPlayBackHotkeyPressed()
        {
            PlayEvents_Click(null, null);
        }
    }
}
