using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.ViewModels
{
    public class EventTypeDataTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if(!(item is InputEvent inputEvent) || !(container is FrameworkElement frameworkElement)) return base.SelectTemplate(item, container);
            
            switch (inputEvent.EventCase)
            {
                case InputEvent.EventOneofCase.KeyboardEvent:
                    return frameworkElement.FindResource("KeyEventTemplate") as DataTemplate;
                case InputEvent.EventOneofCase.MouseEvent:
                    return base.SelectTemplate(item, container);
                case InputEvent.EventOneofCase.None:
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
        }
    }
}
