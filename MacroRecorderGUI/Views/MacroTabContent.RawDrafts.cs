using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private bool _rawEachEdited;
    private bool HasRawDraft => new Control[] { RawDelay, RawFixedDuration, RawX, RawY, RawFlags, RawData, RawKey, RawRelative, RawDesktop, RawKeyUp }
        .Any(_rawDrafts.IsEdited);

    private bool CanLeaveRawDraft()
    {
        if (!HasRawDraft && !_rawEachEdited) return true;
        try
        {
            if (HasRawDraft) _ = ReadRawDraft();
            if (_rawEachEdited) _ = ReadRawTime(RawEachDelay.Text);
            Status = "Exact input has unapplied changes. Open Exact input and apply or discard them before continuing.";
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or FormatException)
        { Status = "Exact input: " + error.Message + " Correct or discard the draft before continuing."; }
        return false;
    }

    private ProtobufInputEvent ReadRawDraft()
    {
        if (_rawEvent is not { } input || _macro?.Events.Contains(input) != true)
            throw new ArgumentException("The edited event is no longer available. Discard its draft before continuing.");
        var value = input.OriginalProtobufInputEvent.Clone();
        if (_rawDrafts.IsEdited(RawDelay)) value.TimeSinceLastEvent = ReadRawTime(RawDelay.Text);
        if (value.Delay is { } delay && _rawDrafts.IsEdited(RawFixedDuration))
        {
            delay.DurationMicroseconds = ReadRawTime(RawFixedDuration.Text);
            MacroRecorderGUI.Event.DelayEvent.ValidateDuration(delay.DurationMicroseconds);
        }
        if (value.MouseEvent is { } mouse)
        {
            if (_rawDrafts.IsEdited(RawX)) mouse.X = int.Parse(RawX.Text, CultureInfo.InvariantCulture);
            if (_rawDrafts.IsEdited(RawY)) mouse.Y = int.Parse(RawY.Text, CultureInfo.InvariantCulture);
            if (_rawDrafts.IsEdited(RawFlags)) mouse.ActionType = uint.Parse(RawFlags.Text, CultureInfo.InvariantCulture);
            if (_rawDrafts.IsEdited(RawData)) mouse.WheelRotation = uint.Parse(RawData.Text, CultureInfo.InvariantCulture);
            if (_rawDrafts.IsEdited(RawRelative)) mouse.RelativePosition = RawRelative.IsChecked == true;
            if (_rawDrafts.IsEdited(RawDesktop)) mouse.MappedToVirtualDesktop = RawDesktop.IsChecked == true;
        }
        else if (value.KeyboardEvent is { } key)
        {
            if (_rawDrafts.IsEdited(RawKey)) key.VirtualKeyCode = uint.Parse(RawKey.Text, CultureInfo.InvariantCulture);
            if (_rawDrafts.IsEdited(RawKeyUp)) key.KeyUp = RawKeyUp.IsChecked == true;
        }
        return value;
    }

    private static ulong ReadRawTime(string text) => string.IsNullOrWhiteSpace(text) ? 0 : ulong.Parse(text, CultureInfo.InvariantCulture);

    private void RawTime_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox field && string.IsNullOrWhiteSpace(field.Text)) field.Text = "0";
    }

    private void RawDiscard_Click(object sender, RoutedEventArgs e)
    {
        _rawDrafts.Clear(); _rawEachEdited = false;
        RawEachDelay.Text = "5000"; _rawEachEdited = false;
        LoadRaw(); Status = "Exact input drafts discarded.";
    }
}
