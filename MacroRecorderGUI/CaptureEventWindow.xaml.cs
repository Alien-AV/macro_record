﻿using ProtobufGenerated;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MacroRecorderGUI
{
    /// <summary>
    /// Interaction logic for CaptureEventWindow.xaml
    /// </summary>
    public partial class CaptureEventWindow : Window
    {
        public CaptureEventWindow()
        {
            InitializeComponent();
  
            Mouse_X.Foreground = Brushes.Silver;
            Mouse_Y.Foreground = Brushes.Silver;
            KeyboardEvent_Instructions.Foreground = Brushes.Blue;
            KeyboardEvent_Instructions.Visibility = Visibility.Hidden;

            //Create a timer with interval of 2 secs 
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 2);
        }
        private DispatcherTimer dispatcherTimer;

        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            //Things which happen after 1 timer interval
            
            KeyboardEvent_Instructions.Visibility = System.Windows.Visibility.Collapsed;

            //Disable the timer
            dispatcherTimer.IsEnabled = false;
        }

        private void Mouse_X_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Mouse_X.Text == "")
            {
                Mouse_X.Text = "X";
                Mouse_X.Foreground = Brushes.Silver;
            }
        }

        private void Mouse_X_GotFocus(object sender, RoutedEventArgs e)
        {
            if (Mouse_X.Text == "X")
            {
                Mouse_X.Text = "";
                Mouse_X.Foreground = Brushes.Black;
            }

        }

        private void Mouse_Y_GotFocus(object sender, RoutedEventArgs e)
        {
            if (Mouse_Y.Text == "Y")
            {
                Mouse_Y.Text = "";
                Mouse_Y.Foreground = Brushes.Black;
            }
        }

        private void Mouse_Y_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Mouse_Y.Text == "")
            {
                Mouse_Y.Text = "Y";
                Mouse_Y.Foreground = Brushes.Silver;
            }
        }

        private void CaptureKeyboardEventButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardEvent_Instructions.Visibility = Visibility.Visible;
            dispatcherTimer.Start();
            this.KeyDown += CaptureEventWindow_KeyEvent;
            this.KeyUp += CaptureEventWindow_KeyEvent;
        }
        private InputEvent GetKeyKeyboardEvent(KeyEventArgs e)
        {
            InputEvent capturedKeyboadEvent = new InputEvent {
                KeyboardEvent = new InputEvent.Types.KeyboardEventType
                {
                    KeyUp = e.IsUp,
                    VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(e.Key))
                },
                TimeSinceStartOfRecording = 0
            };
            ListBoxItem item = new ListBoxItem();
            item.Content = "Keyboard Event: KeyUp: " + capturedKeyboadEvent.KeyboardEvent.KeyUp.ToString() + " Key code: " + capturedKeyboadEvent.KeyboardEvent.VirtualKeyCode.ToString();
            KeyboardEvent_listbox.Items.Add(item);

            return capturedKeyboadEvent;
        }
        private void CaptureEventWindow_KeyUp(object sender, KeyEventArgs e)
        {
            KeyboardEvent_Instructions.Visibility = Visibility.Hidden;
            
            LocalKeyboardEvents.Add(GetKeyKeyboardEvent(e));
            if (e.IsUp == true)
            {
                this.KeyUp -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
                this.KeyDown -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
            }           
        }

        private void CaptureEventWindow_KeyEvent(object sender, KeyEventArgs e)
        {
            KeyboardEvent_Instructions.Visibility = Visibility.Hidden;
            LocalKeyboardEvents.Add(GetKeyKeyboardEvent(e));

            if (e.IsUp == true)
            {
                this.KeyUp -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
                this.KeyDown -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
            }
        }
        private List<InputEvent> LocalKeyboardEvents = new List<InputEvent>();

        private void Button_CancelEvent_Click(object sender, RoutedEventArgs e)
        {
            KeyboardEvent_Instructions.Visibility = Visibility.Hidden;
            this.KeyUp -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
            this.KeyDown -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
        }

        private void Button_RemoveEvent_Copy_Click(object sender, RoutedEventArgs e)
        {
            KeyboardEvent_Instructions.Visibility = Visibility.Hidden;
            this.KeyUp -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
            this.KeyDown -= new KeyEventHandler(CaptureEventWindow_KeyEvent);
            LocalKeyboardEvents.Clear();
            KeyboardEvent_listbox.Items.Clear();
        }

        private void Button_AddEvent_Copy_Click(object sender, RoutedEventArgs e)
        {
            //TODO: Use data binding
            foreach (var keyboardItem in KeyboardEvent_listbox.Items)
            {
                ListBoxItem item = new ListBoxItem();
                item.Content = keyboardItem;
                EventListBox.Items.Add(item);
            }
                        
            KeyboardEvent_listbox.Items.Clear();
        }
    }
}
