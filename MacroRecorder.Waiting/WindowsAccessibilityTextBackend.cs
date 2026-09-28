using System.Runtime.InteropServices;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

/// <summary>Read-only, window-scoped UI Automation. Construction does not query the desktop.</summary>
public sealed class WindowsAccessibilityTextBackend : IAccessibilityTextBackend
{
    internal const int MaximumElements = 512;
    internal const int MaximumDepth = 16;
    internal const int MaximumTextLength = 32768;
    private readonly Func<IWindowsAccessibilityApi> _createApi;
    private readonly object _gate = new();
    private Task? _pending;
    private bool _disposed;

    public WindowsAccessibilityTextBackend() : this(() => new WindowsAccessibilityNativeApi()) { }
    internal WindowsAccessibilityTextBackend(Func<IWindowsAccessibilityApi> createApi) => _createApi = createApi;

    public ValueTask<TextReadResult> ReadAsync(WaitWindow window, AccessibilityTextCondition condition, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(condition);
        // The caller may continue editing the protobuf while this request is on the MTA.
        var definition = condition.Clone();
        return new(Start(token, () => Read(window, definition, token),
            (status, detail) => new TextReadResult(status, Detail: detail)));
    }

    public ValueTask<AccessibilityChoicesResult> GetChoicesAsync(WaitWindow window, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new(Start(token, () => GetChoices(window, token),
            (status, detail) => new AccessibilityChoicesResult(status, [], detail)));
    }

    private Task<T> Start<T>(CancellationToken token, Func<T> operation, Func<ReadStatus, string, T> failure)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
            if (_pending is { IsCompleted: false })
                return Task.FromResult(failure(ReadStatus.Unavailable, "An accessibility request is still running."));

            _pending = WindowsAccessibilityWorker.Run(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var result = operation();
                    token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (WindowsAccessibilityReadException error) { return failure(error.Status, error.Message); }
                catch (Exception error) when (error is COMException or ExternalException or InvalidOperationException
                    or ArgumentException or NotSupportedException or OutOfMemoryException or System.ComponentModel.Win32Exception)
                {
                    // No partial text/identity escapes an incomplete native operation or its cleanup.
                    return failure(ReadStatus.Unavailable, $"Accessibility provider unavailable (0x{error.HResult:X8}).");
                }
            }, () => failure(ReadStatus.Unavailable, "The accessibility worker is still running another request."));
            return (Task<T>)_pending;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            return _pending is null ? ValueTask.CompletedTask : new(AwaitCleanup(_pending));
        }
    }

    private static async Task AwaitCleanup(Task pending)
    {
        // The request reports its own failure. Disposal still waits for COM release on the MTA.
        try { await pending.ConfigureAwait(false); } catch { }
    }

    private TextReadResult Read(WaitWindow window, AccessibilityTextCondition definition, CancellationToken token)
    {
        ValidateSelector(definition.Element);
        if (definition.Ancestors.Count > MaximumDepth || !Enum.IsDefined(definition.Source))
            throw new WindowsAccessibilityReadException(ReadStatus.Error, "Invalid accessibility source or ancestor count.");
        foreach (var ancestor in definition.Ancestors) ValidateSelector(ancestor);

        using var api = _createApi();
        var before = Scan(api, window, token);
        var selected = Select(before, definition);
        RequireReadable(selected.Info);
        foreach (var ancestor in selected.Ancestors) RequireReadable(ancestor);
        ValidateWindow(api, window, token);
        RequireSame(selected.Info, api.GetInfo(selected.Element), token);
        token.ThrowIfCancellationRequested();
        var text = api.ReadText(selected.Element, definition.Source, MaximumTextLength + 1);
        token.ThrowIfCancellationRequested();
        if (!text.Complete || text.Text.Length > MaximumTextLength)
            throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility text is incomplete or exceeds 32768 characters.");
        RequireSame(selected.Info, api.GetInfo(selected.Element), token);

        // Resolve again, including uniqueness and the exact parent chain. A stale retained element
        // can still answer property calls after its replacement has appeared in the live tree.
        var after = Scan(api, window, token);
        var reselected = Select(after, definition);
        RequireSame(before[0].Info, after[0].Info, token, allowPassword: true);
        RequireSame(selected.Info, reselected.Info, token);
        if (!selected.Ancestors.SequenceEqual(reselected.Ancestors))
            throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility ancestry changed during the read.");
        ValidateWindow(api, window, token);
        return new(ReadStatus.Success, text.Text, $"{window.Identity}/uia/{selected.Info.RuntimeIdentity}");
    }

    private AccessibilityChoicesResult GetChoices(WaitWindow window, CancellationToken token)
    {
        using var api = _createApi();
        var before = Scan(api, window, token);
        var after = Scan(api, window, token);
        if (before.Count != after.Count || before.Where((entry, index) =>
            entry.Info != after[index].Info || !entry.Ancestors.SequenceEqual(after[index].Ancestors)).Any())
            throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility tree changed while listing choices.");

        var choices = new List<AccessibilityChoice>();
        foreach (var entry in after)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Info.IsPassword || entry.Ancestors.Any(ancestor => ancestor.IsPassword)) continue;
            var selector = ToSelector(entry.Info);
            var ancestors = entry.Ancestors.Select(ToSelector).ToArray();
            // Only present selectors that resolve uniquely; sibling duplicates are not actionable.
            if (after.Count(candidate => Matches(candidate, selector, ancestors)) != 1) continue;
            var label = Label(entry.Info);
            if (ancestors.Length > 0) label += " ← " + string.Join(" ← ", entry.Ancestors.Select(Label));
            if (label.Length > 1024) label = label[..1023] + "…";
            choices.Add(new(label, selector, ancestors));
        }
        ValidateWindow(api, window, token);
        return new(ReadStatus.Success, choices);
    }

    private static string Label(WindowsAccessibilityInfo info) =>
        string.IsNullOrEmpty(info.AutomationId) ? $"Control {info.ControlType}" : $"{info.AutomationId} (control {info.ControlType})";

    private static AccessibilitySelector ToSelector(WindowsAccessibilityInfo info) =>
        new() { AutomationId = info.AutomationId, ControlType = info.ControlType };

    private static void ValidateSelector(AccessibilitySelector? selector)
    {
        if (selector is null || (selector.AutomationId.Length == 0 && selector.ControlType == 0)
            || selector.AutomationId.Length > MaximumTextLength)
            throw new WindowsAccessibilityReadException(ReadStatus.Error, "An accessibility selector must identify an automation ID or control type.");
    }

    private static Entry Select(List<Entry> entries, AccessibilityTextCondition definition)
    {
        Entry? selected = null;
        foreach (var entry in entries)
        {
            if (!Matches(entry, definition.Element, definition.Ancestors)) continue;
            if (selected is not null)
                throw new WindowsAccessibilityReadException(ReadStatus.Error, "The accessibility selector is ambiguous.");
            selected = entry;
        }
        return selected ?? throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "The accessibility element is unavailable.");
    }

    private static bool Matches(Entry entry, AccessibilitySelector selector, IReadOnlyList<AccessibilitySelector> ancestors) =>
        Matches(entry.Info, selector) && ancestors.Count <= entry.Ancestors.Length
        && ancestors.Select((ancestor, index) => Matches(entry.Ancestors[index], ancestor)).All(match => match);

    private static bool Matches(WindowsAccessibilityInfo info, AccessibilitySelector selector) =>
        (selector.ControlType == 0 || selector.ControlType == info.ControlType)
        && (selector.AutomationId.Length == 0 || string.Equals(selector.AutomationId, info.AutomationId, StringComparison.Ordinal));

    private static void RequireReadable(WindowsAccessibilityInfo info)
    {
        if (info.IsPassword)
            throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Password controls cannot be read.");
    }

    private static void RequireSame(WindowsAccessibilityInfo before, WindowsAccessibilityInfo after, CancellationToken token, bool allowPassword = false)
    {
        token.ThrowIfCancellationRequested();
        if (!allowPassword) RequireReadable(after);
        if (before != after)
            throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility element identity changed during the read.");
    }

    private static void ValidateWindow(IWindowsAccessibilityApi api, WaitWindow window, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (window.Handle == 0 || window.ProcessId == 0 || window.ProcessCreated == 0 || !api.IsWindowCurrent(window))
            throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "The selected window or process is no longer available.");
        token.ThrowIfCancellationRequested();
    }

    private static List<Entry> Scan(IWindowsAccessibilityApi api, WaitWindow window, CancellationToken token)
    {
        ValidateWindow(api, window, token);
        var root = api.OpenWindow(window.Handle);
        if (root == 0) throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "The selected window has no accessibility element.");
        var entries = new List<Entry>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        Visit(root, []);
        ValidateWindow(api, window, token);
        return entries;

        void Visit(nint element, WindowsAccessibilityInfo[] ancestors)
        {
            token.ThrowIfCancellationRequested();
            if (entries.Count >= MaximumElements || ancestors.Length > MaximumDepth)
                throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility enumeration exceeds the 512-element or 16-level limit.");
            var info = api.GetInfo(element);
            if (ancestors.Length == 0 && (info.ProcessId != window.ProcessId || info.NativeWindow != window.Handle))
                throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility root no longer identifies the selected window.");
            if (string.IsNullOrEmpty(info.RuntimeIdentity) || info.AutomationId.Length > MaximumTextLength
                || info.ControlType == 0 || !identities.Add(info.RuntimeIdentity))
                throw new WindowsAccessibilityReadException(ReadStatus.Unavailable, "Accessibility identity is missing, repeated, or incomplete.");
            entries.Add(new(element, info, ancestors));
            token.ThrowIfCancellationRequested();
            var child = api.FirstChild(element);
            var childAncestors = new[] { info }.Concat(ancestors).ToArray();
            while (child != 0)
            {
                Visit(child, childAncestors);
                token.ThrowIfCancellationRequested();
                child = api.NextSibling(child);
            }
        }
    }

    private sealed record Entry(nint Element, WindowsAccessibilityInfo Info, WindowsAccessibilityInfo[] Ancestors);
}

internal sealed record WindowsAccessibilityInfo(string AutomationId, uint ControlType, string RuntimeIdentity,
    bool IsPassword = false, uint ProcessId = 0, nint NativeWindow = 0);
internal sealed record WindowsAccessibilityText(string Text, bool Complete = true);

/// <summary>Each instance and every returned native reference belongs to one MTA request.</summary>
internal interface IWindowsAccessibilityApi : IDisposable
{
    bool IsWindowCurrent(WaitWindow window);
    nint OpenWindow(nint window);
    nint FirstChild(nint element);
    nint NextSibling(nint element);
    WindowsAccessibilityInfo GetInfo(nint element);
    WindowsAccessibilityText ReadText(nint element, AccessibilityTextSource source, int maximumLength);
}

internal sealed class WindowsAccessibilityReadException(ReadStatus status, string message) : Exception(message)
{
    internal ReadStatus Status { get; } = status;
}
