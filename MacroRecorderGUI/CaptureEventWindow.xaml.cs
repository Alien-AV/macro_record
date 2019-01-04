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
            Keyboard_EventTxt.Foreground = Brushes.Silver;
            Mouse_X.Foreground = Brushes.Silver;
            Mouse_Y.Foreground = Brushes.Silver;
        }

        private void Keyboard_EventTxt_GotFocus(object sender, RoutedEventArgs e)
        {
            if(Keyboard_EventTxt.Text == "Keyboard Event")
            {
                Keyboard_EventTxt.Text = "";
                Keyboard_EventTxt.Foreground = Brushes.Black;
            }
                
        }

        private void Keyboard_EventTxt_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Keyboard_EventTxt.Text == "")
            {
                Keyboard_EventTxt.Text = "Keyboard Event";
                Keyboard_EventTxt.Foreground = Brushes.Silver;
            }
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
    }
}
