using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using InjectAndCaptureDllEnums;

namespace MacroRecorderGUI.Models
{
    public class MainWindowModel
    {
        // consider what should be moved here
        // probably all the capture-related stuff, in form of a class that inits capture engine, raises events when getting callbacks
        
        // and the inject-related stuff in form of a separate class? it's already present in InjectAndCaptureDLL.cs, should it really appear again?
        // answer: the relevant functions should be imported inside the class only, so InjectAndCaptureDLL.cs won't be needed anymore.
        // the dll api won't be available outside the "inject" class
    }
}