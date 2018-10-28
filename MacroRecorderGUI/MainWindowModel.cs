using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using InjectAndCaptureDllEnums;

namespace MacroRecorderGUI
{
    public class MainWindowModel
    {
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private InjectAndCaptureDll.CaptureEventCallback _captureEventCallbackDelegate;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private InjectAndCaptureDll.StatusCallback _statusCallbackDelegate;

        public MainWindowModel()
        {
            MacroTabs = new ObservableCollection<MacroTab> {new MacroTab(new Macro(), "macro1"), new MacroTab(new Macro(), "macro2")};
            InitCaptureEngine();
        }

        public class MacroTab
        {
            public Macro Macro { get; }

            public MacroTab(Macro macro, string name)
            {
                Macro = macro;
                Name = name;
            }

            public string Name { get; set; }
        }
        public ObservableCollection<MacroTab> MacroTabs { get; set; }

        public int SelectedTabIndex { get; set; } = 0;
        public bool LoopIndefinitely { get; set; } = false;

        private void CaptureEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = ProtobufGenerated.InputEvent.Parser.ParseFrom(evtBuf);

            Application.Current.Dispatcher.Invoke(()=> MacroTabs[SelectedTabIndex].Macro.AddEvent(parsedEvent));
        }

        private void StatusCb(InjectAndCaptureDllEnums.StatusCode statusCode)
        {
            if (statusCode == StatusCode.PlaybackFinished)
            {
                //TODO: publish an event here?
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (LoopIndefinitely) MacroTabs[SelectedTabIndex].Macro.PlayMacro();
                });
            }
            else
            {
                MessageBox.Show("Status reported: \"" + statusCode + "\".");
            }
        }


        private void InitCaptureEngine()
        {
            _statusCallbackDelegate = StatusCb;
            _captureEventCallbackDelegate = CaptureEventCb;

            InjectAndCaptureDll.Init(_captureEventCallbackDelegate, _statusCallbackDelegate);
        }

    }
}