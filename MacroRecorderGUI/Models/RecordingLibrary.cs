using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.Models;

public enum RecordingSaveState { Unsaved, Dirty, Saving, Saved, Failed }

public sealed record RecordingLibraryItem(Guid Id, string Name, bool IsDraft, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, int EventCount, string DurationMicroseconds, bool HasKeyboard, bool HasMouse)
{
    public string Summary => $"{EventCount} raw events · {(double)BigInteger.Parse(DurationMicroseconds) / 1_000_000:0.###} seconds";
    public bool Matches(string? query) => string.IsNullOrWhiteSpace(query)
        || $"{Name} {Summary} {(IsDraft ? "draft" : "recording")} {(HasKeyboard ? "keyboard" : "")} {(HasMouse ? "mouse" : "")}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record StoredRecording(RecordingLibraryItem Metadata, byte[] MacroBytes, bool Recovered = false);
public sealed record RecordingLibraryRead(IReadOnlyList<RecordingLibraryItem> Items, IReadOnlyList<string> Warnings);

public interface IRecordingLibraryStore
{
    Task<RecordingLibraryRead> ListAsync(CancellationToken cancellationToken = default);
    Task<StoredRecording> LoadAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(StoredRecording recording, CancellationToken cancellationToken = default);
}

/// <summary>One atomic document per recording; its payload is the unchanged .macro wire format.</summary>
public sealed class RecordingLibraryStore : IRecordingLibraryStore
{
    public const int MaximumMacroBytes = 64 * 1024 * 1024;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroRecorder", "Recordings");

    public RecordingLibraryStore(string? directory = null) => _directory = Path.GetFullPath(directory ?? DefaultDirectory);

    public async Task<RecordingLibraryRead> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return new([], []);
        var items = new List<RecordingLibraryItem>();
        var warnings = new List<string>();
        var ids = Directory.EnumerateFiles(_directory).Select(Path.GetFileName)
            .Where(name => name!.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!.Split('.')[0]).Where(name => Guid.TryParseExact(name, "N", out _)).Distinct().ToArray();
        foreach (var name in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var record = await LoadAsync(Guid.ParseExact(name, "N"), cancellationToken).ConfigureAwait(false);
                items.Add(record.Metadata);
                if (record.Recovered) warnings.Add($"Recovered {record.Metadata.Name} from its previous saved copy. Save it to repair the current copy.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                warnings.Add($"Could not read recording {name}: {error.Message}");
            }
        }
        return new(items.OrderByDescending(item => item.UpdatedAt).ToArray(), warnings);
    }

    public async Task<StoredRecording> LoadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = RecordPath(id);
            try { return await ReadAsync(path, id, cancellationToken).ConfigureAwait(false); }
            catch (Exception primaryError) when (primaryError is not OperationCanceledException)
            {
                try { return (await ReadAsync(path + ".bak", id, cancellationToken).ConfigureAwait(false)) with { Recovered = true }; }
                catch (Exception backupError) when (backupError is not OperationCanceledException)
                { throw new IOException("Neither saved copy can be read. The files have been retained.", new AggregateException(primaryError, backupError)); }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(StoredRecording recording, CancellationToken cancellationToken = default)
    {
        Validate(recording);
        var document = new LibraryDocument(1, recording.Metadata, recording.MacroBytes,
            Convert.ToHexString(SHA256.HashData(recording.MacroBytes)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = RecordPath(recording.Metadata.Id);
            // A corrupt current file must not replace the only healthy recovery copy.
            var backup = path + ".bak";
            if (File.Exists(path))
            {
                try { await ReadAsync(path, recording.Metadata.Id, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException) { backup = null; }
            }
            await AtomicFile.WriteAsync(path, bytes, backup, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private string RecordPath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A recording ID is required.", nameof(id));
        return Path.Combine(_directory, $"{id:N}.json");
    }

    private static async Task<StoredRecording> ReadAsync(string path, Guid id, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > MaximumMacroBytes * 2L) throw new InvalidDataException("The recording exceeds the library size limit.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<LibraryDocument>(bytes) ?? throw new InvalidDataException("Missing recording document.");
        if (document.Schema != 1 || document.Metadata?.Id != id || document.MacroBytes is null
            || document.Sha256 != Convert.ToHexString(SHA256.HashData(document.MacroBytes)))
            throw new InvalidDataException("Recording identity, version, or integrity check failed.");
        var recording = new StoredRecording(document.Metadata, document.MacroBytes);
        Validate(recording);
        return recording;
    }

    internal static RecordingLibraryItem Describe(Guid id, string name, bool isDraft, DateTimeOffset created,
        DateTimeOffset updated, byte[] bytes)
    {
        if (bytes.Length > MaximumMacroBytes) throw new InvalidDataException("Macros must be 64 MB or smaller.");
        var events = SerializeEvents.DeserializeEventsFromByteArray(bytes).ToArray();
        var duration = events.Aggregate(BigInteger.Zero, (total, input) => total + input.TimeSinceLastEvent);
        return new(id, name, isDraft, created, updated, events.Length, duration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            events.Any(input => input.Type == Event.InputEvent.InputEventType.KeyboardEvent),
            events.Any(input => input.Type == Event.InputEvent.InputEventType.MouseEvent));
    }

    private static void Validate(StoredRecording recording)
    {
        var metadata = recording.Metadata;
        if (metadata.Id == Guid.Empty || string.IsNullOrWhiteSpace(metadata.Name) || metadata.Name.Length > 200)
            throw new InvalidDataException("A recording requires an ID and a name of 1–200 characters.");
        var actual = Describe(metadata.Id, metadata.Name, metadata.IsDraft, metadata.CreatedAt, metadata.UpdatedAt, recording.MacroBytes);
        if (actual != metadata) throw new InvalidDataException("Recording metadata does not match its input payload.");
    }

    private sealed record LibraryDocument(int Schema, RecordingLibraryItem Metadata, byte[] MacroBytes, string Sha256);
}
