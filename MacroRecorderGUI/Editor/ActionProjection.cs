using System.Collections.ObjectModel;
using System.Numerics;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using Windows.System;

namespace MacroRecorderGUI.Editor;

public readonly record struct PathSample(int Index, BigInteger Time, PathPosition? Position, bool StartsSegment, int Segment,
    int? OriginEventIndex = null);

/// <summary>Streaming, lossless interpretation. All ranges partition the input in original order.</summary>
public sealed class ActionProjection
{
    public const ulong MovementPause = 250_000;
    public ObservableCollection<RecordedAction> Actions { get; } = [];
    public List<PathSample> Samples { get; } = [];
    public List<PathSample> RenderSamples { get; } = [];
    private readonly List<int> _renderIndices = [];
    public int RenderIndexFor(int eventIndex) => _renderIndices[eventIndex];
    public long RenderRevision { get; private set; }
    private readonly List<PathSample> _originSamples = [];
    public List<RecordedAction> MouseLandmarks { get; } = [];
    public BigInteger TotalTime { get; private set; }
    public int ProcessedCount => Samples.Count;
    public int StepCount { get; private set; }
    public int IncompleteActionCount { get; private set; }
    public bool HasRelativeMovement { get; private set; }
    public int MovementSpaceCount => _bounds.Count;
    private readonly Dictionary<CoordinateSpace, PathBounds> _bounds = [];
    public PathBounds? BoundsFor(CoordinateSpace space) => _bounds.TryGetValue(space, out var bounds) ? bounds : null;
    public bool IsCurrent(RecordedAction action) => ReferenceEquals(ActionAt(action.Start), action);
    private readonly HashSet<uint> _keys = [];
    private int _buttons;
    private RecordedAction? _active;
    private ActionKind _candidate;
    private int _button;
    private bool _moved;
    private MouseEvent? _previousMouse;
    private PathPosition? _position;
    private readonly HashSet<uint> _chord = [];
    private BigInteger _wheelTotal;
    private int _segment;
    private bool _anomalous;
    private bool _originSegmentPending;

    public void BeginPointerSegment(PointerOriginBoundary origin)
    {
        if (_active is { } active) { UpdateKeyLabels(active); active.Notify(); _active = null; }
        TotalTime += origin.DelayMicroseconds;
        _position = origin.Position is { Frame: PointerCoordinateFrame.PhysicalScreenPixels } point
            ? new PathPosition(point.X, point.Y, origin.AdoptedEvent is not null && !origin.SetupEvent().MouseEvent.MappedToVirtualDesktop
                ? CoordinateSpace.AbsolutePrimary : CoordinateSpace.AbsoluteDesktop) : null;
        _segment++;
        var marker = new PathSample(origin.EventIndex - 1, TotalTime, _position, true, _segment, OriginEventIndex: origin.EventIndex);
        _originSamples.Add(marker); RenderSamples.Add(marker); RenderRevision++;
        if (_position is { } position)
            _bounds[position.Space] = _bounds.TryGetValue(position.Space, out var bounds) ? bounds.Include(position)
                : new(position.X, position.Y, position.X, position.Y);
        _originSegmentPending = true;
        _previousMouse = null;
    }

    public void Reset()
    {
        Actions.Clear(); StepCount = 0; Samples.Clear(); _originSamples.Clear(); RenderSamples.Clear(); _renderIndices.Clear(); RenderRevision++;
        MouseLandmarks.Clear(); _keys.Clear(); _chord.Clear();
        TotalTime = 0; _buttons = 0; _active = null; _position = null; _previousMouse = null; _segment = 0;
        _bounds.Clear(); IncompleteActionCount = 0; HasRelativeMovement = false; _anomalous = false;
        _originSegmentPending = false;
    }

    public void Append(InputEvent input)
    {
        var index = Samples.Count;
        var timeBefore = TotalTime;
        TotalTime += input.TimeSinceLastEvent;
        if (input is DelayEvent fixedDelay) TotalTime += fixedDelay.DurationMicroseconds;
        var followsOrigin = _originSegmentPending;
        var startsSegment = followsOrigin;
        var renderStartsSegment = false;
        _originSegmentPending = false;
        if (input is MouseEvent mouse && HasMove(mouse))
        {
            var space = Space(mouse);
            var changesFrame = _position is null || _position.Value.Space != space;
            startsSegment |= changesFrame;
            renderStartsSegment = changesFrame;
            if (changesFrame) _segment++;
            if (space == CoordinateSpace.RelativeCounts)
            {
                var previous = startsSegment ? new PathPosition(0, 0, space) : _position!.Value;
                _position = new(previous.X + mouse.X, previous.Y + mouse.Y, space);
            }
            else _position = new(mouse.X, mouse.Y, space);
            HasRelativeMovement |= mouse.RelativePosition;
            var position = _position.Value;
            _bounds[space] = _bounds.TryGetValue(space, out var bounds) ? bounds.Include(position)
                : new(position.X, position.Y, position.X, position.Y);
        }
        Samples.Add(new(index, TotalTime, _position, startsSegment, _segment));
        var previousRender = RenderSamples.Count == 0 ? (PathSample?)null : RenderSamples[^1];
        var renderSample = Samples[^1] with { StartsSegment = followsOrigin ? renderStartsSegment : startsSegment };
        _renderIndices.Add(RenderSamples.Count); RenderSamples.Add(renderSample); RenderRevision++;

        var neutral = _keys.Count == 0 && _buttons == 0;
        if (input is DelayEvent delay)
        {
            if (_active is { } previous) { UpdateKeyLabels(previous); previous.Notify(); }
            AddAction(new RecordedAction(index, input, timeBefore)
            {
                Count = 1, Kind = ActionKind.Delay, Complete = true,
                Duration = delay.DurationMicroseconds, Name = "Delay", Detail = "Fixed delay",
                Description = "Wait, then continue the sequence"
            });
            _active = null; _previousMouse = null;
            return;
        }
        if (input is WaitConditionEvent wait)
        {
            if (_active is { } previous) { UpdateKeyLabels(previous); previous.Notify(); }
            var waitAction = new RecordedAction(index, input, timeBefore) { Count = 1,
                Kind = ActionKind.Wait, Complete = neutral, Name = "Wait until…", Detail = wait.Description,
                Description = wait.Description };
            AddAction(waitAction);
            if (!neutral) IncompleteActionCount++;
            _active = null; _previousMouse = null;
            return;
        }
        var continuation = CanContinue(input);
        if (!continuation)
        {
            if (_active is { } previous) { UpdateKeyLabels(previous); previous.Notify(); }
            _active = new(index, input, timeBefore);
            _candidate = ActionKind.Raw;
            _moved = false; _chord.Clear(); _button = 0; _wheelTotal = 0; _anomalous = false;
            if (neutral)
            {
                if (input is KeyboardEvent { KeyUp: false }) _candidate = ActionKind.Keys;
                else if (input is MouseEvent m)
                {
                    if (m.ActionType == MouseActionTypeFlags.Move) _candidate = ActionKind.Move;
                    else if (IsWheel(m)) _candidate = ActionKind.Scroll;
                    else if (DownButton(m) is var button && button != 0 && IsSingleButton(m, down: true))
                    { _candidate = ActionKind.Click; _button = button; }
                }
            }
            AddAction(_active);
        }
        var action = _active!;
        if (action.Count > 0 && !action.Complete) IncompleteActionCount--;
        if (action.Count > 0) action.Duration += input.TimeSinceLastEvent;
        action.Count++;

        if (input is KeyboardEvent key)
        {
            if (key.KeyUp) _anomalous |= !_keys.Remove(key.VirtualKeyCode);
            else { _keys.Add(key.VirtualKeyCode); _chord.Add(key.VirtualKeyCode); }
        }
        else if (input is MouseEvent m)
        {
            if (HasMove(m)) action.ObserveMovement(previousRender, renderSample);
            var down = DownButton(m); var up = UpButton(m);
            _anomalous |= (down & _buttons) != 0 || (up & ~(_buttons | down)) != 0;
            _buttons |= down;
            _buttons &= ~up;
            _moved |= HasMove(m);
            _previousMouse = m;
            if (IsWheel(m)) _wheelTotal += unchecked((int)m.MouseData);
        }

        action.Complete = !_anomalous && (_candidate is ActionKind.Move or ActionKind.Scroll
            || (_candidate is ActionKind.Click or ActionKind.Keys or ActionKind.Sequence && _buttons == 0 && _keys.Count == 0));
        if (!action.Complete) IncompleteActionCount++;
        action.Kind = _candidate == ActionKind.Sequence ? ActionKind.Sequence
            : action.Complete ? (_candidate == ActionKind.Click && _moved ? ActionKind.Drag : _candidate) : ActionKind.Raw;
        action.Detail = action.Kind switch
        {
            ActionKind.Move => Space((MouseEvent)input) == CoordinateSpace.RelativeCounts ? "Move · device counts" : "Move · absolute pixels",
            ActionKind.Click => $"{ButtonName(_button)} click",
            ActionKind.Drag => $"Drag · {ButtonName(_button).ToLowerInvariant()} button",
            ActionKind.Scroll => $"Scroll · {(((MouseEvent)input).ActionType == MouseActionTypeFlags.HorizontalWheel ? "horizontal" : "vertical")} · {_wheelTotal:+0;-0;0} total wheel units",
            ActionKind.Keys => "Keys · " + string.Join(" + ", _chord.OrderBy(code => IsModifier(code) ? 0 : 1).ThenBy(code => code).Select(KeyName)),
            ActionKind.Sequence => "Input sequence · mixed keys/buttons" + (action.Complete ? "" : " · incomplete"),
            _ => "Raw input · incomplete or mixed sequence"
        };
        action.Name = action.Kind switch
        {
            ActionKind.Move => "Move pointer",
            ActionKind.Click => $"{ButtonName(_button)} click",
            ActionKind.Drag => $"{ButtonName(_button)} drag",
            ActionKind.Scroll => _wheelTotal == 0 ? "Scroll"
                : ((MouseEvent)input).ActionType == MouseActionTypeFlags.HorizontalWheel
                    ? (_wheelTotal > 0 ? "Scroll right" : "Scroll left")
                    : (_wheelTotal > 0 ? "Scroll up" : "Scroll down"),
            ActionKind.Keys => "Press " + string.Join(" + ", _chord.OrderBy(code => IsModifier(code) ? 0 : 1).ThenBy(code => code).Select(KeyName)),
            ActionKind.Sequence => "Mixed input sequence",
            _ => "Raw input"
        };
        action.Description = action.Kind switch
        {
            ActionKind.Move or ActionKind.Drag or ActionKind.Click => _position is { } p
                ? p.Space == CoordinateSpace.RelativeCounts ? "Relative movement · device counts"
                    : $"{(p.Space == CoordinateSpace.AbsoluteDesktop ? "Virtual desktop" : "Primary screen")} · {p.X:0}, {p.Y:0} px"
                : "Position not recorded",
            ActionKind.Keys => $"{action.Count:N0} key transitions · original order",
            ActionKind.Scroll => $"{_wheelTotal:+0;-0;0} wheel units · {(((MouseEvent)input).ActionType == MouseActionTypeFlags.HorizontalWheel ? "horizontal" : "vertical")}",
            ActionKind.Sequence => "Mixed keys and buttons · original order",
            _ => "Inspect exact captured input"
        };
        if (action.Kind is ActionKind.Click or ActionKind.Drag) MouseLandmarks.Add(action);
    }

    private void AddAction(RecordedAction action)
    {
        action.Number = Actions.Count + 1;
        StepCount += action.Wait > 0 ? 2 : 1;
        action.StepNumber = StepCount;
        Actions.Add(action);
    }

    public void FlushNotifications()
    {
        if (_active is { } action) { UpdateKeyLabels(action); action.Notify(); }
    }

    private void UpdateKeyLabels(RecordedAction action)
    {
        action.KeyLabels = action.Kind == ActionKind.Keys
            ? _chord.OrderBy(code => IsModifier(code) ? 0 : 1).ThenBy(code => code).Select(KeyName).ToArray() : [];
    }

    private bool CanContinue(InputEvent input)
    {
        if (_active is null) return false;
        if (_keys.Count > 0 || _buttons != 0)
        {
            var keys = _candidate == ActionKind.Keys && _buttons == 0 && input is KeyboardEvent k && (!k.KeyUp || _keys.Contains(k.VirtualKeyCode));
            var button = _candidate == ActionKind.Click && _buttons == _button && _keys.Count == 0 && input is MouseEvent m
                && (m.ActionType == MouseActionTypeFlags.Move || IsSingleButton(m, down: false) && UpButton(m) == _button);
            if (!keys && !button) _candidate = ActionKind.Sequence;
            return true;
        }
        if (input.TimeSinceLastEvent >= MovementPause) return false;
        if (input is not MouseEvent next || _previousMouse is null) return false;
        if (_candidate == ActionKind.Move)
            return next.ActionType == MouseActionTypeFlags.Move && Space(next) == Space(_previousMouse);
        return _candidate == ActionKind.Scroll && IsWheel(next) && next.ActionType == _previousMouse.ActionType
            && Math.Sign(unchecked((int)next.MouseData)) == Math.Sign(unchecked((int)_previousMouse.MouseData));
    }

    public RecordedAction? ActionAt(int index)
    {
        var low = 0; var high = Actions.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2; var a = Actions[mid];
            if (index < a.Start) high = mid - 1;
            else if (index >= a.End) low = mid + 1;
            else return a;
        }
        return null;
    }

    public RecordedAction? ActionForSample(PathSample sample) => ActionAt(sample.OriginEventIndex ?? sample.Index);

    // Sample-and-hold is deliberate: interpolating between device reports would invent motion during waits.
    public PathSample? SampleAt(BigInteger time)
    {
        var low = 0; var high = Samples.Count - 1; var result = -1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (Samples[mid].Time <= time) { result = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return result < 0 ? null : Samples[result];
    }

    public PathSample? PointerAt(BigInteger time)
    {
        var input = SampleAt(time);
        var low = 0; var high = _originSamples.Count - 1; var result = -1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (_originSamples[mid].Time <= time) { result = mid; low = mid + 1; }
            else high = mid - 1;
        }
        if (result < 0) return input;
        var origin = _originSamples[result];
        return input is null || origin.Time > input.Value.Time
            || origin.Time == input.Value.Time && origin.Index >= input.Value.Index ? origin : input;
    }

    // Render order includes origin markers before their event. A time lookup alone
    // can cross a zero-delay wait and expose pointer input from after it.
    public PathSample? PointerBeforeEvent(int index)
    {
        if (index < 0 || index >= _renderIndices.Count) return null;
        var before = _renderIndices[index] - 1;
        return before < 0 ? null : RenderSamples[before];
    }

    public static bool HasMove(MouseEvent m) => (m.ActionType & MouseActionTypeFlags.Move) != 0;
    // Playback interprets an omitted legacy payload as X1; other payloads use the low two bits.
    internal static uint ExtraButtonMask(MouseEvent mouse) => (mouse.MouseData == 0 ? 1u : mouse.MouseData) & 3;
    private static bool IsModifier(uint code) => code is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
    public static string KeyName(uint code) => code switch
    {
        0x11 => "Ctrl", 0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl",
        0x10 => "Shift", 0xA0 => "Left Shift", 0xA1 => "Right Shift",
        0x12 => "Alt", 0xA4 => "Left Alt", 0xA5 => "Right Alt",
        >= 0x30 and <= 0x39 => ((char)code).ToString(),
        _ => Enum.IsDefined((VirtualKey)code) ? ((VirtualKey)code).ToString() : $"VK 0x{code:X2}"
    };
    public static CoordinateSpace Space(MouseEvent m) => m.RelativePosition ? CoordinateSpace.RelativeCounts
        : m.MappedToVirtualDesktop ? CoordinateSpace.AbsoluteDesktop : CoordinateSpace.AbsolutePrimary;
    private static bool IsWheel(MouseEvent m) => m.ActionType is MouseActionTypeFlags.Wheel or MouseActionTypeFlags.HorizontalWheel;
    private static int DownButton(MouseEvent m) => Buttons(m, true);
    private static int UpButton(MouseEvent m) => Buttons(m, false);
    private static int Buttons(MouseEvent m, bool down)
    {
        var flags = m.ActionType; var result = 0;
        if ((flags & (down ? MouseActionTypeFlags.LeftDown : MouseActionTypeFlags.LeftUp)) != 0) result |= 1;
        if ((flags & (down ? MouseActionTypeFlags.RightDown : MouseActionTypeFlags.RightUp)) != 0) result |= 2;
        if ((flags & (down ? MouseActionTypeFlags.MiddleDown : MouseActionTypeFlags.MiddleUp)) != 0) result |= 4;
        if ((flags & (down ? MouseActionTypeFlags.XDown : MouseActionTypeFlags.XUp)) != 0)
            result |= (int)ExtraButtonMask(m) << 3;
        return result;
    }
    private static bool IsSingleButton(MouseEvent m, bool down) => down
        ? m.ActionType is MouseActionTypeFlags.LeftDown or MouseActionTypeFlags.RightDown or MouseActionTypeFlags.MiddleDown
            || m.ActionType == MouseActionTypeFlags.XDown && m.MouseData is 0 or 1 or 2
        : m.ActionType is MouseActionTypeFlags.LeftUp or MouseActionTypeFlags.RightUp or MouseActionTypeFlags.MiddleUp
            || m.ActionType == MouseActionTypeFlags.XUp && m.MouseData is 0 or 1 or 2;
    private static string ButtonName(int button) => button switch { 1 => "Left", 2 => "Right", 4 => "Middle", 8 => "X1", 16 => "X2", _ => "Mouse" };
}
