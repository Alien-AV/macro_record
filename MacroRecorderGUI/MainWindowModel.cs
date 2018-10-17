using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MacroRecorderGUI
{

    internal sealed class MainWindowModel {
        private static readonly Lazy<MainWindowModel> lazy = new Lazy<MainWindowModel>(() => new MainWindowModel());

        public static MainWindowModel Instance { get { return lazy.Value; } }

        private ObservableCollection<ProtobufGenerated.InputEvent> ReleasedHotkeysObsColl = new ObservableCollection<ProtobufGenerated.InputEvent>();


        private MainWindowModel() {
            InitReleasedHotkeyCodes();
        }

        private void InitReleasedHotkeyCodes() {

            Key[] fakeKeys = {
                Key.LeftShift,
                Key.LeftCtrl,
                Key.LeftAlt,
                Key.RightShift,
                Key.RightCtrl,
                Key.RightAlt
            };

            foreach (var keyCode in fakeKeys) {
                AddReleasedHotkey(keyCode);
            }

        }

        private void AddReleasedHotkey(Key vkey) {
            ReleasedHotkeysObsColl.Add(new ProtobufGenerated.InputEvent {
                KeyboardEvent = new ProtobufGenerated.InputEvent.Types.KeyboardEventType {
                    KeyUp = true,
                    VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(vkey))
                }
            });
        }

        internal void InsertReleasedHotkeysAtEnd(ObservableCollection<ProtobufGenerated.InputEvent> eventObsColl) {
            foreach (var item in ReleasedHotkeysObsColl) {
                eventObsColl.Add(item);
            }
        }

        internal void InsertReleasedHotkeysAtStart(ObservableCollection<ProtobufGenerated.InputEvent> eventObsColl) {
            var startIdx = 0;

            foreach (var item in ReleasedHotkeysObsColl) {
                eventObsColl.Insert(startIdx, item);
            }

        }
    }
}
