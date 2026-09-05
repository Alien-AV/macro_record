using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent : UserControl
{
    private MacroViewModel? _subscribedViewModel;

    public MacroTabContent()
    {
        InitializeComponent();
        DataContextChanged += MacroTabContent_DataContextChanged;
    }

    private MacroViewModel? ViewModel => DataContext as MacroViewModel;

    private void MacroTabContent_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Events.CollectionChanged -= Events_CollectionChanged;
        }

        _subscribedViewModel = args.NewValue as MacroViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Events.CollectionChanged += Events_CollectionChanged;
        }

        UpdateEmptyState();
    }

    private void Events_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _subscribedViewModel?.Events.Count > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

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
