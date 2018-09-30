using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Google.Protobuf;

namespace MacroRecorderGUI
{
    public partial class MainWindow : Window
    {
        private readonly InjectAndCaptureDll.IacDllCaptureEventCb _captureEventCbDelegate;
        public ObservableCollection<InputEvent> EventsObsColl = new ObservableCollection<InputEvent>();
        private void Iac_Dll_Capture_Event_Cb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = InputEvent.Parser.ParseFrom(evtBuf);

            Dispatcher.Invoke(()=> EventsObsColl.Add(parsedEvent));
        }

        public MainWindow()
        {
            InitializeComponent();
            InjectAndCaptureDll.iac_dll_init();
            EventsListBox.ItemsSource = EventsObsColl;
            _captureEventCbDelegate = Iac_Dll_Capture_Event_Cb;
        }

        private void StartRecord_Click(object sender, RoutedEventArgs e)
        {
               InjectAndCaptureDll.iac_dll_start_capture(_captureEventCbDelegate);
        }

        private void StopRecordButton_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.iac_dll_stop_capture();
        }

        private void InjectButton_Click(object sender, RoutedEventArgs e)
        {
            var serializedEvents = new InputEventList();
            
            serializedEvents.InputEvents.AddRange(EventsObsColl);
            
            var serializedEventsByteArray = serializedEvents.ToByteArray();
            var sizeOfCppBuffer = Marshal.SizeOf(serializedEventsByteArray[0]) * serializedEventsByteArray.Length;
            var cppBuffer = Marshal.AllocHGlobal(sizeOfCppBuffer);
            try
            {
                Marshal.Copy(serializedEventsByteArray, 0, cppBuffer, sizeOfCppBuffer);
                InjectAndCaptureDll.iac_dll_inject_events(cppBuffer, sizeOfCppBuffer);
            }
            finally
            {
                Marshal.FreeHGlobal(cppBuffer);
            }
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = EventsListBox.SelectedItems.Cast<InputEvent>().ToList();
            foreach (var eventToRemove in selectedItems)
            {
                EventsObsColl.Remove(eventToRemove);
            }
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            EventsObsColl.Clear();
        }
    }
}
