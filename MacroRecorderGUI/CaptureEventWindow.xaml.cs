using ProtobufGenerated;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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

        public Point MousePosition { get; set; }
        private Thread mousePositionTracker;
        static private volatile bool trackMoust = false;
        static public bool TrackMouse { get { return trackMoust; } set { trackMoust = value; } }

        private DispatcherTimer dispatcherTimer;
        private List<InputEvent> LocalKeyboardEvents = new List<InputEvent>();
        private List<InputEvent> LocalMouseEvents = new List<InputEvent>();
        
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
            //TODO: move this to different function
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


        static private uint GetMouseAction(string MouseAction)
        {
            switch (MouseAction)
            {
                case "MouseMove":
                    return 0x0001;
                case "LeftDown":
                    return 0x0001;
                case "LeftUp":
                    return 0x0004;
                case "RightDown":
                    return 0x0008;
                case "RightUp":
                    return 0x0010;
                case "MiddleDown":
                    return 0x0020;
                case "MiddleUp":
                    return 0x0040;
                case "XDown":
                    return 0x0080;
                case "XUp":
                    return 0x0100;
                default:
                    return 0;
            }
        }

        private void CaptureMouseEventButton_Click(object sender, RoutedEventArgs e)
        {
            TrackMouse = true;
            mousePositionTracker = new Thread(MouseTracker);

            Mouse_X.Foreground = Brushes.Black;
            Mouse_Y.Foreground = Brushes.Black;

            this.KeyDown += CaptureMouseEvent;

            mousePositionTracker.Start();
        }

        private void CaptureMouseEvent(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                TrackMouse = false;
            }
            InputEvent CurrentMouseEvent = new InputEvent
            {
                MouseEvent = new InputEvent.Types.MouseEventType
                {
                    X = (int)MousePosition.X,//Convert.ToInt32(Mouse_X.Text),
                    Y = (int)MousePosition.Y,
                    ActionType = GetMouseAction(((ComboBoxItem)Mouse_ActionType.SelectedItem).Content.ToString())
                },
                TimeSinceStartOfRecording = 0
            };
            LocalMouseEvents.Add(CurrentMouseEvent);
            //TODO: is this the way its done?
            this.KeyDown -= CaptureMouseEvent;
            
        }

        //TODO: try catch for when the window is closed 
        //TODO: 
        private void MouseTracker()
        {
            while(TrackMouse)
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    //Point pt = new Point();
                    //GetCursorPos(ref pt);
                    MousePosition = PointToScreen(Mouse.GetPosition(this));

                    Mouse_X.Text = MousePosition.X.ToString();
                    Mouse_Y.Text = MousePosition.Y.ToString();

                }), DispatcherPriority.ContextIdle, null);
            }
        }
    }
}
