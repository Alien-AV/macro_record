using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

/// <summary>
/// Minimal Windows SDK UIAutomationClient.h ABI. Raw owned interface pointers avoid RCWs whose
/// finalizers could release provider objects from an unrelated apartment. No UI framework is needed.
/// </summary>
internal sealed class WindowsAccessibilityNativeApi : IWindowsAccessibilityApi
{
    private readonly List<nint> _references = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private nint _automation, _walker;
    private bool _initialized, _disposed;
    private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9"); // CUIAutomation8
    private static readonly Guid AutomationInterface = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a"); // IUIAutomation2
    private static readonly Guid TextInterface = new("32eba289-3583-42c9-9c59-3b6d9a1e9b6a");
    private static readonly Guid ValueInterface = new("a94cd8b1-0844-4cd6-9d2d-640537ab39e9");

    internal WindowsAccessibilityNativeApi()
    {
        Check(CoInitializeEx(0, 0)); // COINIT_MULTITHREADED
        _initialized = true;
        try
        {
            var classId = AutomationClass;
            var interfaceId = AutomationInterface;
            var result = CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var automation);
            _automation = Own(automation, result, required: true);
            // Finite provider timeouts are not permission to abandon a blocked COM call. The
            // backend task remains pending through that call and release, retaining the runner slot.
            Check(Method<PutUInt>(_automation, 61)(_automation, 1000)); // ConnectionTimeout
            Check(Method<PutUInt>(_automation, 63)(_automation, 1000)); // TransactionTimeout
            result = Method<GetPointer>(_automation, 16)(_automation, out var walker); // RawViewWalker
            _walker = Own(walker, result, required: true);
        }
        catch { Dispose(); throw; }
    }

    public bool IsWindowCurrent(WaitWindow window)
    {
        AssertThread();
        if (!IsWindow(window.Handle) || GetWindowThreadProcessId(window.Handle, out var processId) == 0
            || processId != window.ProcessId) return false;
        using var process = OpenProcess(0x00101000, false, processId); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || WaitForSingleObject(process, 0) != 258
            || !GetProcessTimes(process, out var created, out _, out _, out _)
            || created != window.ProcessCreated) return false;
        return IsWindow(window.Handle) && GetWindowThreadProcessId(window.Handle, out processId) != 0
            && processId == window.ProcessId;
    }

    public nint OpenWindow(nint window)
    {
        AssertThread();
        var result = Method<Navigate>(_automation, 6)(_automation, window, out var element); // ElementFromHandle
        return Own(element, result, required: true);
    }

    public nint FirstChild(nint element) => Walk(element, 4);
    public nint NextSibling(nint element) => Walk(element, 6);
    private nint Walk(nint element, int slot)
    {
        AssertThread();
        var result = Method<Navigate>(_walker, slot)(_walker, element, out var relative);
        return Own(relative, result);
    }

    public WindowsAccessibilityInfo GetInfo(nint element)
    {
        AssertThread();
        var password = Integer(element, 35) != 0;
        var automationId = String(element, 29, WindowsAccessibilityTextBackend.MaximumTextLength);
        if (!automationId.Complete) throw Unavailable("Accessibility automation ID exceeds the supported length.");
        var controlType = Integer(element, 21);
        var processId = Integer(element, 20);
        Check(Method<GetPointer>(element, 36)(element, out var window)); // CurrentNativeWindowHandle (UIA_HWND)
        return new(automationId.Text, unchecked((uint)controlType), RuntimeIdentity(element), password,
            unchecked((uint)processId), window);
    }

    public WindowsAccessibilityText ReadText(nint element, AccessibilityTextSource source, int maximumLength)
    {
        AssertThread();
        if (Integer(element, 35) != 0) throw Unavailable("Password controls cannot be read.");
        switch (source)
        {
            case AccessibilityTextSource.AccessibleName:
                return String(element, 23, maximumLength);
            case AccessibilityTextSource.ValuePattern:
                return String(Pattern(element, 10002, ValueInterface), 4, maximumLength);
            case AccessibilityTextSource.TextPattern:
                var pattern = Pattern(element, 10014, TextInterface);
                var result = Method<GetPointer>(pattern, 7)(pattern, out var document); // DocumentRange, not selection/visible range
                var range = Own(document, result, required: true);
                // Requesting cap + 1 distinguishes complete text at the cap from truncation.
                return ReadBstr((out nint text) => Method<GetText>(range, 12)(range, maximumLength, out text), maximumLength);
            default: throw new WindowsAccessibilityReadException(ReadStatus.Error, "Unknown accessibility text source.");
        }
    }

    private nint Pattern(nint element, int patternId, Guid interfaceId)
    {
        var result = Method<GetPattern>(element, 14)(element, patternId, ref interfaceId, out var pattern);
        return Own(pattern, result, required: true);
    }

    private static int Integer(nint element, int slot)
    {
        Check(Method<GetInt>(element, slot)(element, out var result));
        return result;
    }

    private static WindowsAccessibilityText String(nint element, int slot, int maximumLength) =>
        ReadBstr((out nint text) => Method<GetPointer>(element, slot)(element, out text), maximumLength);

    internal delegate int ReadPointer(out nint value);

    internal static WindowsAccessibilityText ReadBstr(ReadPointer get, int maximumLength, Action<nint>? release = null)
    {
        nint text = 0;
        try
        {
            Check(get(out text));
            return CopyString(text, maximumLength);
        }
        finally { if (text != 0) (release ?? Marshal.FreeBSTR)(text); }
    }

    private static WindowsAccessibilityText CopyString(nint text, int maximumLength)
    {
        if (text == 0) return new(""); // A null BSTR is the COM representation of an empty string.
        var length = SysStringLen(text);
        // Name/Value getters have no length parameter. Inspect the BSTR length before copying
        // into managed memory; never materialize an oversized provider string in managed memory.
        return new(Marshal.PtrToStringUni(text, (int)Math.Min(length, (uint)maximumLength))!, length <= maximumLength);
    }

    private static string RuntimeIdentity(nint element) =>
        ReadRuntimeId((out nint array) => Method<GetPointer>(element, 4)(element, out array));

    internal static string ReadRuntimeId(ReadPointer get, Action<nint>? release = null)
    {
        nint array = 0;
        try
        {
            Check(get(out array));
            if (array == 0 || SafeArrayGetDim(array) != 1) throw Unavailable("Missing accessibility runtime ID.");
            Check(SafeArrayGetVartype(array, out var type));
            Check(SafeArrayGetLBound(array, 1, out var lower));
            Check(SafeArrayGetUBound(array, 1, out var upper));
            var count = (long)upper - lower + 1;
            if (type != 3 || count < 1 || count > 128) throw Unavailable("Invalid accessibility runtime ID."); // VT_I4
            var values = new int[(int)count];
            Check(SafeArrayAccessData(array, out var data));
            try { Marshal.Copy(data, values, 0, values.Length); }
            finally { Check(SafeArrayUnaccessData(array)); }
            return string.Join(".", values.Select(value => value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
        }
        finally
        {
            if (array != 0)
            {
                if (release is null) Check(SafeArrayDestroy(array));
                else release(array);
            }
        }
    }

    private nint Own(nint pointer, int result, bool required = false)
    {
        if (pointer != 0)
        {
            try { _references.Add(pointer); }
            catch { Marshal.Release(pointer); throw; }
        }
        Check(result);
        if (required && pointer == 0) throw Unavailable("Accessibility provider returned no object.");
        return pointer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        AssertThread();
        _disposed = true;
        try
        {
            for (var index = _references.Count - 1; index >= 0; --index) Marshal.Release(_references[index]);
            _references.Clear();
        }
        finally { if (_initialized) { _initialized = false; CoUninitialize(); } }
    }

    private void AssertThread()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI Automation must stay on its owning MTA.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static WindowsAccessibilityReadException Unavailable(string detail) => new(ReadStatus.Unavailable, detail);
    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPointer(nint instance, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInt(nint instance, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PutUInt(nint instance, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Navigate(nint instance, nint element, out nint relative);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPattern(nint instance, int patternId, ref Guid interfaceId, out nint pattern);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetText(nint instance, int maximumLength, out nint text);

    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(nint reserved, uint apartment);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern uint SysStringLen(nint text);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern uint SafeArrayGetDim(nint array);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayGetVartype(nint array, out ushort type);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayGetLBound(nint array, uint dimension, out int bound);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayGetUBound(nint array, uint dimension, out int bound);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayAccessData(nint array, out nint data);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayUnaccessData(nint array);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayDestroy(nint array);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out ulong created, out ulong exited, out ulong kernel, out ulong user);
}
