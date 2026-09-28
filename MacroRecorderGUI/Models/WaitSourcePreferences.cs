using System.Text.Json;
using MacroRecorder.Waiting;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.Models;

internal sealed record WaitSourcePreferenceData(WaitLocalOptions Options, string? Warning = null);

internal interface IWaitSourcePreferenceStore
{
    Task<WaitSourcePreferenceData> LoadAsync(CancellationToken token);
    Task SaveAsync(WaitLocalOptions options, CancellationToken token);
}

/// <summary>App-local permissions and OCR installation paths. Never part of a recording.</summary>
internal sealed class WaitSourcePreferences(IWaitSourcePreferenceStore store, WaitLocalSettings settings)
{
    internal static WaitSourcePreferences Current { get; } = new(new WaitSourcePreferenceStore(), WaitServices.LocalSettings);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsLoaded { get; private set; }
    public string? Warning { get; private set; }
    public WaitLocalOptions Options => IsLoaded ? settings.Options : new();

    // Only normal app startup and explicit settings commands call this. Merely
    // creating a form, importing a macro, or previewing never reads preferences.
    public async Task InitializeAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (IsLoaded) return;
            settings.Options = new();
            var data = await store.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            settings.Options = data.Options;
            Warning = data.Warning;
            IsLoaded = true;
        }
        finally { _gate.Release(); }
    }

    public Task SetMemoryEnabledAsync(bool enabled, CancellationToken token = default) =>
        SaveAsync(options => options with { MemoryEnabled = enabled }, token);

    public Task SetOcrInstallationAsync(string executable, string tessdata, CancellationToken token = default) =>
        SaveAsync(options => options with { TesseractExecutablePath = executable.Trim(), TessdataDirectory = tessdata.Trim() }, token);

    private async Task SaveAsync(Func<WaitLocalOptions, WaitLocalOptions> update, CancellationToken token)
    {
        await InitializeAsync(token);
        await _gate.WaitAsync(token);
        try
        {
            var next = update(settings.Options);
            WaitSourcePreferenceStore.Validate(next);
            await store.SaveAsync(next, token);
            settings.Options = next;
            Warning = null;
        }
        finally { _gate.Release(); }
    }
}

internal sealed class WaitSourcePreferenceStore(string? path = null) : IWaitSourcePreferenceStore
{
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroRecorder", "wait-sources.json"));

    public async Task<WaitSourcePreferenceData> LoadAsync(CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length > 65536) throw new JsonException("Settings exceed the supported size.");
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = json.RootElement;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) throw new JsonException("Duplicate setting.");
            if (root.GetProperty("Schema").GetInt32() != 1) throw new JsonException("Unsupported settings version.");
            var options = new WaitLocalOptions(root.GetProperty("MemoryEnabled").GetBoolean(),
                root.GetProperty("TesseractExecutablePath").GetString() ?? throw new JsonException(),
                root.GetProperty("TessdataDirectory").GetString() ?? throw new JsonException());
            Validate(options);
            return new(options);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return new(new()); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
            or KeyNotFoundException or OverflowException or FormatException or IOException or UnauthorizedAccessException)
        { return new(new(), "Local wait settings could not be read. Memory access is disabled; review and save the source settings again."); }
    }

    public async Task SaveAsync(WaitLocalOptions options, CancellationToken token)
    {
        Validate(options);
        var document = new { Schema = 1, options.MemoryEnabled, options.TesseractExecutablePath, options.TessdataDirectory };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        if (bytes.Length > 65536) throw new ArgumentException("Local wait settings exceed 64 KiB after encoding. Use shorter OCR installation paths.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await AtomicFile.WriteAsync(_path, bytes, cancellationToken: token);
    }

    internal static void Validate(WaitLocalOptions options)
    {
        foreach (var (name, value) in new[] { ("Tesseract executable", options.TesseractExecutablePath), ("Tessdata directory", options.TessdataDirectory) })
            if (value is null || value.Length > 32767 || value.Contains('\0') || value.Length > 0 && !Path.IsPathFullyQualified(value))
                throw new ArgumentException($"{name}: enter a full local path or leave it empty.");
    }
}
