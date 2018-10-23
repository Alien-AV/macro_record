using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
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

        private MainWindowModel _model = MainWindowModel.Instance;

        private void CaptureEventCb(IntPtr evtBufPtr, int bufSize)
        {
            var evtBuf = new byte[bufSize];
            Marshal.Copy(evtBufPtr, evtBuf, 0, bufSize);
            var parsedEvent = ProtobufGenerated.InputEvent.Parser.ParseFrom(evtBuf);

            Dispatcher.Invoke(()=> _currentMacro.AddEvent(parsedEvent));
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
            if (ClearListOnStartRecord.IsChecked == true) ClearList_Click(sender, e);
            InjectAndCaptureDll.StartCapture();
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.StopCapture();
            if (AutoChangeDelay.IsChecked == true) ChangeDelays_Click(sender, e);
        }

        private void PlayEvents_Click(object sender, RoutedEventArgs e)
        {
            _currentMacro.PlayMacro();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = EventsListBox.SelectedItems.Cast<ProtobufGenerated.InputEvent>().ToList();
            _currentMacro.RemoveEvents(selectedItems);
        }

        private void EventsListBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveEvent_Click(sender, e);
            }
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            _currentMacro.Clear();
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
            _currentMacro.ChangeDelays(timeIncrement);
        }
        
        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            _currentMacro.SaveToFile(); //TODO: inject "filesaver" or whatever
        }

        private void LoadEvents_Click(object sender, RoutedEventArgs e)
        {
            _currentMacro.LoadFromFile(); //TODO: inject "fileloader" or whatever
        }

        private void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.InjectEventAbort();
        }

    }
}
