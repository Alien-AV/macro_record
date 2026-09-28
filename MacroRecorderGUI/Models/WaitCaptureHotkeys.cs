namespace MacroRecorderGUI.Models;

internal interface IWaitCaptureRegistration
{
    bool Register(HotkeyGesture gesture, Action<uint> callback, out int id);
    bool Unregister(int id);
}

// All changes run on the shell dispatcher. Failed cleanup remains tracked and inert
// so recording can require a known registration set and disposal can retry release.
internal sealed class WaitCaptureHotkeys(IWaitCaptureRegistration registration, Action<WaitCaptureTarget, uint> capture,
    Func<uint>? messageClock = null) : IDisposable
{
    private readonly Dictionary<int, HotkeyGesture> _registrations = [];
    private readonly Dictionary<int, uint> _lastMessage = [];
    private bool _changing, _disposed, _accepting;
    private uint _enabledAt;
    public WaitCaptureConfiguration Configuration { get; private set; } = WaitCaptureConfiguration.Default;
    public bool IsReady => !_disposed && _accepting;
    public bool HasRegistrations => _registrations.Count > 0;
    public IReadOnlyList<(WaitCaptureTarget Target, HotkeyGesture Gesture)> RegisteredBindings => !_accepting ? []
        : Configuration.Bindings().Where(pair => pair.Binding.Enabled && _registrations.ContainsValue(pair.Binding.Gesture))
            .Select(pair => (pair.Target, pair.Binding.Gesture)).ToArray();

    public bool TryApply(WaitCaptureConfiguration configuration, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        configuration.Validate();
        error = null;
        var previousReady = _accepting;
        _changing = true; _accepting = false;
        try
        {
            // Clean up any registrations retained after a failed rollback or disable.
            if (!previousReady && !ReleaseAll())
            { error = "A previous capture shortcut could not be released. Capture is disabled; retry or close the app."; return false; }
            var wanted = Enabled(configuration);
            var added = new List<int>();
            foreach (var gesture in wanted)
            {
                if (_registrations.ContainsValue(gesture)) continue;
                if (!Add(gesture, out var id))
                {
                    error = $"Could not register {gesture.DisplayName}. Another application may be using it.";
                    var rolledBack = Release(added);
                    _accepting = previousReady && rolledBack;
                    if (!rolledBack) error += " Cleanup failed; capture is disabled. Retry or close the app.";
                    return false;
                }
                added.Add(id);
            }
            var removed = new List<HotkeyGesture>();
            foreach (var (id, gesture) in _registrations.ToArray())
            {
                if (wanted.Contains(gesture)) continue;
                if (!Remove(id))
                {
                    var restored = Release(added);
                    foreach (var old in removed) restored = Add(old, out _) && restored;
                    _accepting = previousReady && restored;
                    error = restored ? "Could not release the previous capture shortcut. Previous settings remain active."
                        : "Could not restore the previous shortcuts. Capture is disabled; retry or close the app.";
                    return false;
                }
                removed.Add(gesture);
            }
            Configuration = configuration;
            _enabledAt = messageClock?.Invoke() ?? unchecked((uint)Environment.TickCount);
            _accepting = true;
            return true;
        }
        finally { _changing = false; }
    }

    public void DiscardPendingMessages() => _enabledAt = unchecked((messageClock?.Invoke() ?? (uint)Environment.TickCount) + 1);

    private static HashSet<HotkeyGesture> Enabled(WaitCaptureConfiguration configuration) =>
        configuration.Bindings().Where(pair => pair.Binding.Enabled).Select(pair => pair.Binding.Gesture).ToHashSet();

    private bool Add(HotkeyGesture gesture, out int id)
    {
        var callbackId = -1;
        if (!registration.Register(gesture, time => Dispatch(callbackId, time), out id)) return false;
        callbackId = id; _registrations.Add(id, gesture); return true;
    }
    private bool Remove(int id)
    {
        if (!registration.Unregister(id)) return false;
        _registrations.Remove(id); _lastMessage.Remove(id); return true;
    }
    private bool Release(IEnumerable<int> ids)
    {
        var released = true;
        foreach (var id in ids.ToArray()) released = Remove(id) && released;
        return released;
    }
    private bool ReleaseAll() => Release(_registrations.Keys);
    private void Dispatch(int id, uint time)
    {
        if (_disposed || _changing || !_accepting || !_registrations.TryGetValue(id, out var gesture)) return;
        if (unchecked((int)(time - _enabledAt)) < 0) return; // Ignore queued messages from before configuration/reenabling.
        if (_lastMessage.TryGetValue(id, out var last) && last == time) return;
        var binding = Configuration.Bindings().FirstOrDefault(pair => pair.Binding.Enabled && pair.Binding.Gesture == gesture);
        if (binding.Binding is null) return;
        _lastMessage[id] = time;
        capture(binding.Target, time);
    }
    public void Dispose()
    {
        _disposed = true; _accepting = false;
        ReleaseAll(); // Repeated disposal retries a failed native release; callbacks stay inert.
    }
}
