using System.Text.Json;
using System.Text.Json.Serialization;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUI.Models;

internal sealed record WaitCaptureBinding(
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] VirtualKey Key,
    [property: JsonRequired] HotKeyModifiers Modifiers)
{
    [JsonIgnore] public HotkeyGesture Gesture => new(Key, Modifiers);
}

internal sealed record WaitCaptureConfiguration(
    [property: JsonRequired] WaitCaptureBinding FocusedWindow,
    [property: JsonRequired] WaitCaptureBinding HoveredWindow,
    [property: JsonRequired] WaitCaptureBinding PointerPixel)
{
    public static WaitCaptureConfiguration Default => new(
        new(false, VirtualKey.F6, HotKeyModifiers.Control | HotKeyModifiers.Alt),
        new(false, VirtualKey.F7, HotKeyModifiers.Control | HotKeyModifiers.Alt),
        new(false, VirtualKey.F8, HotKeyModifiers.Control | HotKeyModifiers.Alt));

    public IEnumerable<(WaitCaptureTarget Target, WaitCaptureBinding Binding)> Bindings()
    {
        yield return (WaitCaptureTarget.FocusedWindow, FocusedWindow);
        yield return (WaitCaptureTarget.HoveredWindow, HoveredWindow);
        yield return (WaitCaptureTarget.PointerPixel, PointerPixel);
    }

    public static IReadOnlyList<VirtualKey> Keys { get; } = Enumerable.Range((int)VirtualKey.A, 26)
        .Concat(Enumerable.Range((int)VirtualKey.Number0, 10)).Concat(Enumerable.Range((int)VirtualKey.F1, 24))
        .Select(key => (VirtualKey)key).ToArray();

    public void Validate()
    {
        var used = new HashSet<HotkeyGesture>();
        var reserved = KeyboardShortcuts.EmergencyStopChoices.Concat(
            new[] { KeyboardShortcuts.StartRecording, KeyboardShortcuts.StopRecording, KeyboardShortcuts.StartPlayback }).ToHashSet();
        foreach (var (_, binding) in Bindings())
        {
            if (binding is null || !Keys.Contains(binding.Key)
                || (binding.Modifiers & ~(HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift)) != 0
                || (binding.Modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt)) == 0)
                throw new ArgumentException("Each capture shortcut needs Ctrl or Alt, optional Shift, and a letter, digit, or function key.");
            if (reserved.Contains(binding.Gesture)) throw new ArgumentException($"{binding.Gesture.DisplayName} is reserved for recording, playback, or emergency stop.");
            if (binding.Enabled && !used.Add(binding.Gesture)) throw new ArgumentException("Enabled capture shortcuts must be different.");
        }
    }
}

internal sealed record WaitCapturePreferenceData(WaitCaptureConfiguration Configuration, string? Warning = null);
internal interface IWaitCapturePreferenceStore
{
    Task<WaitCapturePreferenceData> LoadAsync(CancellationToken token = default);
    Task SaveAsync(WaitCaptureConfiguration configuration, CancellationToken token = default);
}

internal sealed class WaitCapturePreferenceStore(string? path = null) : IWaitCapturePreferenceStore
{
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroRecorder", "wait-capture-preferences.json"));

    public async Task<WaitCapturePreferenceData> LoadAsync(CancellationToken token = default)
    {
        try
        {
            const int maximumBytes = 64 * 1024;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytes = new byte[maximumBytes + 1];
            var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken: token);
            if (length > maximumBytes) throw new JsonException("Capture preferences exceed the 64 KiB limit.");
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            RejectDuplicates(document.RootElement);
            if (document.RootElement.GetProperty("Schema").GetInt32() != 1) throw new JsonException("Unsupported preference version.");
            var configuration = document.RootElement.GetProperty("Capture").Deserialize<WaitCaptureConfiguration>() ?? throw new JsonException();
            configuration.Validate();
            return new(configuration);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return new(WaitCaptureConfiguration.Default); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { return new(WaitCaptureConfiguration.Default, "Capture shortcuts could not be loaded and are off. Review and save them in Settings."); }
    }

    public async Task SaveAsync(WaitCaptureConfiguration configuration, CancellationToken token = default)
    {
        configuration.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await AtomicFile.WriteAsync(_path, JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, Capture = configuration }), cancellationToken: token);
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException("Duplicate capture preference field.");
            RejectDuplicates(property.Value);
        }
    }
}
