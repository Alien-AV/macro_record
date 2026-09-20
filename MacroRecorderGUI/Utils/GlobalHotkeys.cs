using System.Runtime.InteropServices;
using Windows.System;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.Utils;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public sealed class GlobalHotkeys : IDisposable
{
    private const uint WindowMessageHotKey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private static readonly UIntPtr SubclassId = new(0x4D524743);

    private readonly nint _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly Dictionary<int, Action<uint>> _handlers = [];
    private readonly Dictionary<int, RecordingStopGestures> _recordingStops = [];
    private int _nextHotKeyId = 9000;
    private bool _disposed;
    private readonly Func<int, VirtualKey, HotKeyModifiers, bool> _register;
    private readonly Func<int, bool> _unregister;
    private int? _emergencyId;
    public HotkeyGesture? EmergencyStop { get; private set; }
    public RecordingStopGestures RegisteredRecordingStops => _recordingStops.Values.Aggregate(
        RecordingStopGestures.None, (result, gesture) => result | gesture);

    public GlobalHotkeys(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _subclassProcedure = WindowSubclassProcedure;
        _register = (id, key, modifiers) => RegisterHotKey(_windowHandle, id, (uint)modifiers | NoRepeat, (uint)key);
        _unregister = id => UnregisterHotKey(_windowHandle, id);

        if (!SetWindowSubclass(_windowHandle, _subclassProcedure, SubclassId, UIntPtr.Zero))
        {
            throw new InvalidOperationException("The window message hook for global shortcuts could not be installed.");
        }
    }

    internal GlobalHotkeys(Func<int, VirtualKey, HotKeyModifiers, bool> register, Func<int, bool> unregister)
    {
        _register = register;
        _unregister = unregister;
        _subclassProcedure = WindowSubclassProcedure;
    }

    public bool TrySetEmergencyStop(HotkeyGesture gesture, Action<RecordingStopCommand> handler, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handler);
        error = null;
        if (!KeyboardShortcuts.EmergencyStopChoices.Contains(gesture))
        { error = "Choose one of the supported emergency-stop shortcuts."; return false; }
        Action<uint> onHotkey = time => handler(new RecordingStopCommand(RecordingStopCommand.For(gesture), time));
        if (gesture == EmergencyStop && _emergencyId is { } existing)
        { _handlers[existing] = onHotkey; return true; }
        var id = _nextHotKeyId++;
        if (!_register(id, gesture.Key, gesture.Modifiers))
        { error = $"Could not register {gesture.DisplayName}. It may be in use by another application."; return false; }
        _handlers.Add(id, onHotkey);
        _recordingStops.Add(id, RecordingStopCommand.For(gesture));
        if (_emergencyId is { } previous)
        {
            if (!_unregister(previous))
            {
                if (_unregister(id)) { _handlers.Remove(id); _recordingStops.Remove(id); }
                error = "Could not release the previous shortcut. The previous emergency stop remains selected.";
                return false;
            }
            _handlers.Remove(previous);
            _recordingStops.Remove(previous);
        }
        _emergencyId = id;
        EmergencyStop = gesture;
        return true;
    }

    public bool AddHotKey(VirtualKey key, HotKeyModifiers modifiers, Action<uint> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = _nextHotKeyId++;
        if (!_register(id, key, modifiers))
        {
            return false;
        }

        _handlers.Add(id, handler);
        _recordingStops.Add(id, RecordingStopCommand.For(new HotkeyGesture(key, modifiers)));
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _handlers.Keys)
        {
            _unregister(id);
        }

        _handlers.Clear();
        _recordingStops.Clear();
        if (_windowHandle != 0) RemoveWindowSubclass(_windowHandle, _subclassProcedure, SubclassId);
        EmergencyStop = null;
        _disposed = true;
    }

    private nint WindowSubclassProcedure(
        nint windowHandle,
        uint message,
        UIntPtr wParam,
        nint lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message == WindowMessageHotKey
            && DispatchHotkey(unchecked((int)wParam.ToUInt64()), unchecked((uint)GetMessageTime())))
        {
            return nint.Zero;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    internal bool DispatchHotkey(int id, uint messageTime)
    {
        if (!_handlers.TryGetValue(id, out var handler)) return false;
        handler(messageTime);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessageTime();

    private delegate nint SubclassProcedure(
        nint windowHandle,
        uint message,
        UIntPtr wParam,
        nint lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, UIntPtr wParam, nint lParam);
}
