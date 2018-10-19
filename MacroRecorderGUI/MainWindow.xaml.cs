using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Google.Protobuf;
using InjectAndCaptureDllEnums;

namespace MacroRecorderGUI
{
    public partial class MainWindow : Window
    {
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly InjectAndCaptureDll.CaptureEventCallback _captureEventCallbackDelegate;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly InjectAndCaptureDll.StatusCallback _statusCallbackDelegate;

        private readonly Macro _currentMacro = new Macro();

        //public ObservableCollection<ProtobufGenerated.InputEvent> Events = new ObservableCollection<ProtobufGenerated.InputEvent>();

        private void CaptureEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = ProtobufGenerated.InputEvent.Parser.ParseFrom(evtBuf);

            Dispatcher.Invoke(()=> _currentMacro.Events.Add(parsedEvent));
        }

        private void StatusCb(InjectAndCaptureDllEnums.StatusCode statusCode)
        {
            if (statusCode == StatusCode.PlaybackFinished)
            {
                //TODO: publish an event here?
                Dispatcher.Invoke(() =>
                {
                    if (LoopIndefinitely.IsChecked == true) PlayEvents_Click(null, null);
                });
            }
            else
            {
                MessageBox.Show("Status reported: \"" + statusCode + "\".");
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _statusCallbackDelegate = StatusCb;
            _captureEventCallbackDelegate = CaptureEventCb;
            EventsListBox.ItemsSource = _currentMacro.Events;
            InjectAndCaptureDll.Init(_captureEventCallbackDelegate, _statusCallbackDelegate);
        }

        private void StartRecord_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.StartCapture();
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.StopCapture();
        }

        private void PlayEvents_Click(object sender, RoutedEventArgs e)
        {
            if (!_currentMacro.Events.Any()) return;

            var serializedEventsByteArray = Macro.SerializeEventsToByteArray(_currentMacro.Events);
            InjectAndCaptureDll.InjectEvents(serializedEventsByteArray);
        }
        
        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = EventsListBox.SelectedItems.Cast<ProtobufGenerated.InputEvent>().ToList();
            foreach (var eventToRemove in selectedItems)
            {
                _currentMacro.Events.Remove(eventToRemove);
            }
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            _currentMacro.Events.Clear();
        }

        private void AllowOnlyNumbersInTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void ChangeDelays_Click(object sender, RoutedEventArgs e)
        {
            if (!DelayTextBox.Text.Any()) return;

            var timeIncrement = Convert.ToUInt64(DelayTextBox.Text);
            var currentTimeOffset = 0ul;
            foreach(var inputEvent in _currentMacro.Events)
            {
                inputEvent.TimeSinceStartOfRecording = currentTimeOffset;
                currentTimeOffset += timeIncrement;
            }
            CollectionViewSource.GetDefaultView(_currentMacro.Events).Refresh(); //TODO: implement the events as wrapper class around protobuf class, and implement PropertyChanged event listeners on them
        }

        private void EventsListBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveEvent_Click(sender, e);
            }
        }

        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            FileOperations.SaveEventsToFile(_currentMacro.Events);
        }

        private void LoadEvents_Click(object sender, RoutedEventArgs e)
        {
            var deserializedEvents = FileOperations.LoadEventsFromFile();
            if (deserializedEvents != null)
            {
                PopulateEventCollectionWithNewEvents(deserializedEvents);
            }
        }

        private void PopulateEventCollectionWithNewEvents(IEnumerable<ProtobufGenerated.InputEvent> deserializedEvents)
        {
            _currentMacro.Events.Clear();
            foreach (var deserializedEvent in deserializedEvents)
            {
                _currentMacro.Events.Add(deserializedEvent);
            }
        }

        private void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.InjectEventAbort();
        }
    }
}
