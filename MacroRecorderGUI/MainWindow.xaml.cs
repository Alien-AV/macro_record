﻿using System;
using System.Collections.Generic;
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
            InjectAndCaptureDll.Init(_captureEventCallbackDelegate,_errorCallbackDelegate);
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
            if (!EventsObsColl.Any())
            {
                return;
            }

            var serializedEventsByteArray = SerializeEventsToByteArray(EventsObsColl);
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

        private void ChangeDelays_Click(object sender, RoutedEventArgs e)
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

        private void EventsListBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveEvent_Click(sender, e);
            }
        }

        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            FileOperations.SaveEventsToFile(EventsObsColl);
        }

        private void LoadEvents_Click(object sender, RoutedEventArgs e)
        {
            var deserializedEvents = FileOperations.LoadEventsFromFile();
            if (deserializedEvents != null)
            {
                PopulateEventCollectionWithNewEvents(deserializedEvents);
            }
        }

        private void PopulateEventCollectionWithNewEvents(IEnumerable<InputEvent> deserializedEvents)
        {
            EventsObsColl.Clear();
            foreach (var deserializedEvent in deserializedEvents)
            {
                EventsObsColl.Add(deserializedEvent);
            }
        }

        internal static byte[] SerializeEventsToByteArray(IEnumerable<InputEvent> inputEventList)
        {
            var serializedEvents = new InputEventList();
            serializedEvents.InputEvents.AddRange(inputEventList);
            var serializedEventsByteArray = serializedEvents.ToByteArray();
            return serializedEventsByteArray;
        }

        internal static IEnumerable<InputEvent> DeserializeEventsFromByteArray(byte[] serializedEvents)
        {
            var deserializedEvents = InputEventList.Parser.ParseFrom(serializedEvents);
            return deserializedEvents.InputEvents;
        }

        private void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.InjectEventAbort();
        }
    }
}
