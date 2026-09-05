using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.ViewModels;

public sealed class EventTypeDataTemplateSelector : DataTemplateSelector
{
    public DataTemplate? KeyboardTemplate { get; set; }
    public DataTemplate? MouseTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return SelectEventTemplate(item);
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectEventTemplate(item);
    }

    private DataTemplate? SelectEventTemplate(object item)
    {
        return item switch
        {
            KeyboardEvent => KeyboardTemplate,
            MouseEvent => MouseTemplate,
            _ => null
        };
    }
}
