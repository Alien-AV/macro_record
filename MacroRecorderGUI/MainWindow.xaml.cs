using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
            var eventString = parsedEvent.ToString();

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
            (new Thread(() =>
            {
                ulong firstEventTime = EventsObsColl[0].TimeSinceStartOfRecording;
                foreach (var evt in EventsObsColl)
                {
                    ulong timeToSleepInNanoseconds = evt.TimeSinceStartOfRecording - firstEventTime;
                    int timeToSleepInMilliseconds = (int)(timeToSleepInNanoseconds / 1000000);

                    var serializedEvent = evt.ToByteArray();
                    var sizeOfCppBuffer = Marshal.SizeOf(serializedEvent[0]) * serializedEvent.Length;
                    var cppBuffer = Marshal.AllocHGlobal(sizeOfCppBuffer);
                    try
                    {
                        Marshal.Copy(serializedEvent, 0, cppBuffer, sizeOfCppBuffer);
                        Thread.Sleep(timeToSleepInMilliseconds);
                        InjectAndCaptureDll.iac_dll_inject_event(cppBuffer, sizeOfCppBuffer);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(cppBuffer);
                    }
                }
            })).Start();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = EventsListBox.SelectedItems.Cast<InputEvent>().ToList();
            foreach (var eventToRemove in selectedItems)
            {
                EventsObsColl.Remove(eventToRemove);
            }
            //EventsObsColl.Remove(EventsListBox.SelectedItem as InputEvent);
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            EventsObsColl.Clear();
        }
    }
}
