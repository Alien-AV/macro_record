using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Google.Protobuf;

namespace MacroRecorderGUI
{
    public partial class MainWindow : Window
    {
        private readonly InjectAndCaptureDll.CaptureEventCallback _captureEventCallbackDelegate;
        private readonly InjectAndCaptureDll.ErrorCallback _errorCallbackDelegate;

        public ObservableCollection<InputEvent> EventsObsColl = new ObservableCollection<InputEvent>();

        private void CaptureEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = InputEvent.Parser.ParseFrom(evtBuf);

            Dispatcher.Invoke(()=> EventsObsColl.Add(parsedEvent));
        }

        private void ErrorCb(string error)
        {
            MessageBox.Show(error);
        }

        public MainWindow()
        {
            InitializeComponent();
            _errorCallbackDelegate = ErrorCb;
            _captureEventCallbackDelegate = CaptureEventCb;
            EventsListBox.ItemsSource = EventsObsColl;
            InjectAndCaptureDll.InitWithErrorCb(_errorCallbackDelegate);
        }

        private void StartRecord_Click(object sender, RoutedEventArgs e)
        {
               InjectAndCaptureDll.StartCapture(_captureEventCallbackDelegate);
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.StopCapture();
        }

        private void PlayEvents_Click(object sender, RoutedEventArgs e)
        {
            if (!EventsObsColl.Any())
            {
                return;
            }

            var serializedEvents = new InputEventList();
            
            serializedEvents.InputEvents.AddRange(EventsObsColl);
            
            var serializedEventsByteArray = serializedEvents.ToByteArray();
            var sizeOfCppBuffer = Marshal.SizeOf(serializedEventsByteArray[0]) * serializedEventsByteArray.Length;
            var cppBuffer = Marshal.AllocHGlobal(sizeOfCppBuffer);
            try
            {
                Marshal.Copy(serializedEventsByteArray, 0, cppBuffer, sizeOfCppBuffer);
                InjectAndCaptureDll.InjectEvents(cppBuffer, sizeOfCppBuffer);
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

        private void AllowOnlyNumbersInTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void ShortenDelays_Click(object sender, RoutedEventArgs e)
        {
            if (!DelayTextBox.Text.Any())
            {
                return;
            }
            var timeIncrement = Convert.ToUInt64(DelayTextBox.Text);
            var currentTimeOffset = 0ul;
            foreach(var inputEvent in EventsObsColl)
            {
                inputEvent.TimeSinceStartOfRecording = currentTimeOffset;
                currentTimeOffset += timeIncrement;
            }
            CollectionViewSource.GetDefaultView(EventsObsColl).Refresh(); //TODO: implement the events as wrapper class around protobuf class, and implement PropertyChanged event listeners on them
        }
    }
}
