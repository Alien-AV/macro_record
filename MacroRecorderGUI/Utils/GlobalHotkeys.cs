using System.Runtime.InteropServices;
using Windows.System;

namespace MacroRecorderGUI.Utils;

[Flags]
internal enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

internal sealed class GlobalHotkeys : IDisposable
{
    private const uint WindowMessageHotKey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private static readonly UIntPtr SubclassId = new(0x4D524743);

    private readonly nint _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextHotKeyId = 9000;
    private bool _disposed;

    public GlobalHotkeys(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _subclassProcedure = WindowSubclassProcedure;

        if (!SetWindowSubclass(_windowHandle, _subclassProcedure, SubclassId, UIntPtr.Zero))
        {
            throw new InvalidOperationException("The window message hook for global shortcuts could not be installed.");
        }
    }

    public bool AddHotKey(VirtualKey key, HotKeyModifiers modifiers, Action handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = _nextHotKeyId++;
        if (!RegisterHotKey(_windowHandle, id, (uint)modifiers | NoRepeat, (uint)key))
        {
            return false;
        }

        _handlers.Add(id, handler);
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
            UnregisterHotKey(_windowHandle, id);
        }

        _handlers.Clear();
        RemoveWindowSubclass(_windowHandle, _subclassProcedure, SubclassId);
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
            && _handlers.TryGetValue(unchecked((int)wParam.ToUInt64()), out var handler))
        {
            handler();
            return nint.Zero;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

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
