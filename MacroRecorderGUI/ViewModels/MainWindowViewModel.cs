using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using InjectAndCaptureDllEnums;
using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly MainWindowModel _mainWindowModel;

        public MainWindowViewModel()
        {
            _mainWindowModel = new MainWindowModel();
            MacroTabs = new ObservableCollection<MacroTab> {new MacroTab(new Macro(), "macro0"), new MacroTab(new Macro(), "macro1")};
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

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged("SelectedTabIndex");}
        }

        public bool LoopPlayback { get; set; }

        public void AddNewTab()
        {
            MacroTabs.Add(new MacroTab(new Macro(), $"macro{MacroTabs.Count}"));
            SelectedTabIndex = MacroTabs.Count - 1;
        }

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private InjectAndCaptureDll.CaptureEventCallback _captureEventCallbackDelegate;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private InjectAndCaptureDll.StatusCallback _statusCallbackDelegate;
        private int _selectedTabIndex;

        public Macro ActiveMacro => MacroTabs[SelectedTabIndex].Macro;

        private void CaptureEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = ProtobufGenerated.InputEvent.Parser.ParseFrom(evtBuf);

            Application.Current.Dispatcher.Invoke(()=> ActiveMacro.AddEvent(parsedEvent));
        }

        private void StatusCb(InjectAndCaptureDllEnums.StatusCode statusCode)
        {
            if (statusCode == StatusCode.PlaybackFinished)
            {
                //TODO: publish an event here?
                if (LoopPlayback) ActiveMacro.PlayMacro();
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