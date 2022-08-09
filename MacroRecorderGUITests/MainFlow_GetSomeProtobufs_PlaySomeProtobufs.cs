using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests
{
    internal class FakeRecordEngine : IRecordEngine
    {
        public event RecordEngine.RecordEventsEventHandler RecordedEvent;
        public event RecordEngine.RecordStatusEventHandler RecordStatus;
        public void StartRecord()
        {
        }

        public void StopRecord()
        {
        }

        protected virtual void OnRecordedEvent(RecordEngine.RecordEventsEventArgs e) => RecordedEvent?.Invoke(this, e);
        
        public void PushEvent(ProtobufInputEvent fakeEvent)
        {
            OnRecordedEvent(new RecordEngine.RecordEventsEventArgs(fakeEvent));
        }

        public static ProtobufInputEvent MakeKeyboardEvent(uint virtualKey, bool keyUp, ulong timeSinceStartOfRecording)
        {
            return new ProtobufInputEvent
            {
                KeyboardEvent = new ProtobufInputEvent.Types.KeyboardEventType
                {
                    KeyUp = keyUp, VirtualKeyCode = virtualKey
                },
                TimeSinceStartOfRecording = timeSinceStartOfRecording
            };
        }

        public static ProtobufInputEvent MakeMouseEvent(int x, int y, uint actionType, bool relativePosition, ulong timeSinceStartOfRecording)
        {
            return new ProtobufInputEvent
            {
                MouseEvent = new ProtobufInputEvent.Types.MouseEventType
                {
                    ActionType = actionType, MappedToVirtualDesktop = false, RelativePosition = relativePosition,
                    WheelRotation = 0, X = x, Y = y
                },
                TimeSinceStartOfRecording = timeSinceStartOfRecording
            };
        }
    }

    internal class FakePlaybackEngine : IPlaybackEngine
    {
        public IEnumerable<InputEvent> PlayedEvents = new List<InputEvent>();
        public void PlaybackEvents(IEnumerable<InputEvent> events)
        {
            PlayedEvents = events;
        }

        public void PlaybackEventAbort()
        {
            throw new NotImplementedException();
        }
    }

    internal class FakeMainWindowViewModel : MainWindowViewModel
    {
        public FakeMainWindowViewModel(IRecordEngine recordEngine, IPlaybackEngine playbackEngine) : base(recordEngine,
            playbackEngine)
        {
        }

        protected override void InvokeDispatcher(Action action)
        {
            action.Invoke();
        }
    }

    [TestClass]
    public class MainFlowTest
    {
        

        [TestMethod]
        public void GetSomeProtobufsPlaySomeProtobufsTest()
        {
            var recordEngine = new FakeRecordEngine();
            var playbackEngine = new FakePlaybackEngine();
            var fakeMainWindowViewModel = new FakeMainWindowViewModel(recordEngine, playbackEngine);

            var expectedEvents = new List<ProtobufInputEvent>
            {
                FakeRecordEngine.MakeKeyboardEvent(100, false, 1000),
                FakeRecordEngine.MakeKeyboardEvent(100, true, 1500),
                FakeRecordEngine.MakeMouseEvent(100, 100, (uint)MouseActionTypeFlags.Move, false, 2000),
                FakeRecordEngine.MakeMouseEvent(100, 100, (uint)MouseActionTypeFlags.LeftDown, false, 2500),
                FakeRecordEngine.MakeMouseEvent(100, 100, (uint)MouseActionTypeFlags.LeftUp, false, 3000)
            };

            foreach (var inputEvent in expectedEvents)
            {
                recordEngine.PushEvent(inputEvent);
            }

            fakeMainWindowViewModel.ActiveMacro.PlayMacro();

            var releaseModKeyProtoInputEvents = ReleaseModifierKeys.ReleaseModKeysEvents.Select(ev => ev.OriginalProtobufInputEvent);
            var expectedEventsWrappedWithReleaseModifierKeys = releaseModKeyProtoInputEvents
                .Concat(expectedEvents)
                .Concat(releaseModKeyProtoInputEvents);

            CollectionAssert.AreEqual(
                expectedEventsWrappedWithReleaseModifierKeys.ToList(),
                playbackEngine.PlayedEvents.Select(inputEvent=>inputEvent.OriginalProtobufInputEvent).ToList()
                );
        }
    }
}
