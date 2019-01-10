using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
        private int _currentHotkeyId = 9000;
        private const int StartRecordHotkeyId = 9000;
        private const int StopRecordHotkeyId = 9001;
        private const int PlayBackHotkeyId = 9002;
        private const int PlayBackAbortHotkeyId = 9003;

        public delegate void HotkeyHandler();

        private struct HotkeyKeysAndHandler
        {
            public uint Vk;
            public uint Mod;
            public HotkeyHandler HotkeyHandler;
        }

        private readonly Dictionary<int, HotkeyKeysAndHandler> _hotkeyById = new Dictionary<int, HotkeyKeysAndHandler>();

        public GlobalHotkeys(MainWindow window)
        {
            _window = window;
            _windowInteropHelper = new WindowInteropHelper(_window);
            _source = HwndSource.FromHwnd(_windowInteropHelper.Handle);
            _source.AddHook(HwndHook);
        }

        public void Dispose()
        {
            _source.RemoveHook(HwndHook);
            _source = null;
            
            UnregisterHotKey();
        }

        public void AddHotKey(Key key, ModifierKeys mod, HotkeyHandler handler)
        {
            var vKey = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(key));
            var modUint = Convert.ToUInt32(mod);
            _hotkeyById.Add(_currentHotkeyId, new HotkeyKeysAndHandler()
                {
                    Vk = vKey,
                    Mod = modUint,
                    HotkeyHandler = handler
                });

            if(!RegisterHotKey(_windowInteropHelper.Handle, _currentHotkeyId, modUint, vKey))
            {
                // handle error
            }

            _currentHotkeyId++;
        }

        private void UnregisterHotKey()
        {
            UnregisterHotKey(_windowInteropHelper.Handle, StartRecordHotkeyId);
        }

        private void RunHotkeyHandlerById(int id)
        {
            _hotkeyById[id].HotkeyHandler();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if(msg == WM_HOTKEY){
                    RunHotkeyHandlerById(wParam.ToInt32());
                    handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
