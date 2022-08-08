using System;
using System.Windows;
using System.Windows.Controls;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.ViewModels
{
    public class EventTypeDataTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if(!(item is InputEvent inputEvent) || !(container is FrameworkElement frameworkElement)) return base.SelectTemplate(item, container);
            
            switch (inputEvent.Type)
            {
                case InputEvent.InputEventType.KeyboardEvent:
                    return frameworkElement.FindResource("KeyEventTemplate") as DataTemplate;
                case InputEvent.InputEventType.MouseEvent:
                    return frameworkElement.FindResource("MouseEventTemplate") as DataTemplate;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
        }
    }
}
