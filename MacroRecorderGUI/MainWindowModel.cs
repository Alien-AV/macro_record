using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace MacroRecorderGUI
{
    internal sealed class MainWindowModel
    {
        private static readonly Lazy<MainWindowModel> LazyInstance =
            new Lazy<MainWindowModel>(() => new MainWindowModel());

        public static MainWindowModel Instance => LazyInstance.Value;

        private static readonly Key[] FakeKeys =
        {
            Key.LeftShift,
            Key.LeftCtrl,
            Key.LeftAlt,
            Key.RightShift,
            Key.RightCtrl,
            Key.RightAlt
        };

        public IEnumerable<ProtobufGenerated.InputEvent> ReleasedHotkeysObsColl = FakeKeys.Select((key) =>
            new ProtobufGenerated.InputEvent
            {
                KeyboardEvent = new ProtobufGenerated.InputEvent.Types.KeyboardEventType
                {
                    KeyUp = true,
                    VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(key))
                }
            });


        private MainWindowModel()
        {
        }
    }
}