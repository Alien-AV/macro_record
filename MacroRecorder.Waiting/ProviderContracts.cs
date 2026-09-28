using ProtobufGenerated;

namespace MacroRecorder.Waiting;

public interface IWaitScheduleEvent
{
    ProtobufInputEvent OriginalProtobufInputEvent { get; }
}

public enum ReadStatus { Success, Unavailable, Error }

/// <summary>Transient window identity. Never persist Handle or ProcessId as a selector.</summary>
public sealed record WaitWindow(nint Handle, uint ProcessId, ulong ProcessCreated)
{
    public string Identity => $"{Handle:X}:{ProcessId}:{ProcessCreated}";
}

/// <summary>Success means a complete bounded read, even when Text is empty.</summary>
public sealed record TextReadResult(ReadStatus Status, string Text = "", string Identity = "", string Detail = "");

public sealed record AccessibilityChoice(string Label, AccessibilitySelector Element,
    IReadOnlyList<AccessibilitySelector> Ancestors);
public sealed record AccessibilityChoicesResult(ReadStatus Status, IReadOnlyList<AccessibilityChoice> Choices, string Detail = "");

/// <summary>One occurrence owns this backend until reads and cancellation callbacks finish.</summary>
public interface IAccessibilityTextBackend : IAsyncDisposable
{
    ValueTask<TextReadResult> ReadAsync(WaitWindow window, AccessibilityTextCondition condition, CancellationToken token);
    ValueTask<AccessibilityChoicesResult> GetChoicesAsync(WaitWindow window, CancellationToken token);
}

/// <summary>Local application policy; never serialized in a macro.</summary>
public sealed record WaitLocalOptions(bool MemoryEnabled = false, string TesseractExecutablePath = "", string TessdataDirectory = "");

public sealed class WaitLocalSettings
{
    private WaitLocalOptions _options = new();
    public WaitLocalOptions Options
    {
        get => Volatile.Read(ref _options);
        set => Volatile.Write(ref _options, value ?? throw new ArgumentNullException(nameof(value)));
    }
    public bool MemoryEnabled
    {
        get => Options.MemoryEnabled;
        set { WaitLocalOptions before; do { before = Options; } while (!ReferenceEquals(Interlocked.CompareExchange(ref _options, before with { MemoryEnabled = value }, before), before)); }
    }
}

public static partial class WaitServices
{
    public static WaitLocalSettings LocalSettings { get; } = new();
}
