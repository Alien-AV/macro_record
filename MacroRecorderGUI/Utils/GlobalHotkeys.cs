using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MacroRecorderGUI.Utils
{
    public class GlobalHotkeys: IDisposable
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
        private readonly WindowInteropHelper _windowInteropHelper;
        private readonly MainWindow _window;
        private const int StartRecordHotkeyId = 9000;
        private const int StopRecordHotkeyId = 9001;
        private const int PlayBackHotkeyId = 9002;
        private const int PlayBackAbortHotkeyId = 9003;

        public GlobalHotkeys(MainWindow window)
        {
            _window = window;
            _windowInteropHelper = new WindowInteropHelper(_window);
            _source = HwndSource.FromHwnd(_windowInteropHelper.Handle);
            _source.AddHook(HwndHook);
            RegisterHotKey();
        }

        public void Dispose()
        {
            _source.RemoveHook(HwndHook);
            _source = null;
            UnregisterHotKey();
        }

        private void RegisterHotKey()
        {
            const uint VK_Q = 0x51;
            const uint VK_W = 0x57;
            const uint VK_E = 0x45;
            const uint VK_R = 0x52;
            const uint MOD_CTRL = 0x0002;
            if(!RegisterHotKey(_windowInteropHelper.Handle, StartRecordHotkeyId, MOD_CTRL, VK_Q))
            {
                // handle error
            }
            if(!RegisterHotKey(_windowInteropHelper.Handle, StopRecordHotkeyId, MOD_CTRL, VK_W))
            {
                // handle error
            }
            if(!RegisterHotKey(_windowInteropHelper.Handle, PlayBackHotkeyId, MOD_CTRL, VK_E))
            {
                // handle error
            }
            if(!RegisterHotKey(_windowInteropHelper.Handle, PlayBackAbortHotkeyId, MOD_CTRL, VK_R))
            {
                // handle error
            }
        }

        private void UnregisterHotKey()
        {
            UnregisterHotKey(_windowInteropHelper.Handle, StartRecordHotkeyId);
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
                        case PlayBackAbortHotkeyId:
                            OnPlayBackAbortHotkeyPressed();
                            handled = true;
                            break;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private void OnStartRecordHotkeyPressed()
        {
            _window.StartRecord_Click(null, null);      //TODO: change this, and remove from implementation from .xaml.cs
        }
        private void OnStopRecordHotkeyPressed()
        {
            _window.StopRecord_Click(null, null);
        }
        private void OnPlayBackHotkeyPressed()
        {
            _window.PlayEvents_Click(null, null);
        }
        private void OnPlayBackAbortHotkeyPressed()
        {
            _window.AbortPlayback_Click(null, null);
        }
    }
}
