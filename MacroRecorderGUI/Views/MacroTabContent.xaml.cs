using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent : UserControl
{
    public MacroTabContent()
    {
        InitializeComponent();
    }

    private MacroViewModel? ViewModel => DataContext as MacroViewModel;

    private void EventsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel?.ReplaceSelection(EventsList.SelectedItems.OfType<InputEvent>());
    }

    private void EventsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete)
        {
            return;
        }

        ViewModel?.RemoveSelectedEvents();
        e.Handled = true;
    }

    private void UnsignedNumberTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        args.Cancel = args.NewText.Any(character => !char.IsDigit(character));
    }

    private void SignedNumberTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        args.Cancel = args.NewText
            .Where((character, index) => character != '-' || index != 0)
            .Any(character => !char.IsDigit(character));
    }
}
