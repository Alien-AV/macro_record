using System.Text.Json;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.Models;

public sealed record DeletedRecording(RecordingLibraryItem Metadata, DateTimeOffset DeletedAt);
public sealed record RecordingTrashRead(IReadOnlyList<DeletedRecording> Items, IReadOnlyList<string> Warnings);
public sealed record RecordingTrashChange(RecordingLibraryItem Metadata, IReadOnlyList<string> Warnings);

public interface IRecordingTrashStore
{
    Task<RecordingTrashRead> ListTrashAsync(CancellationToken cancellationToken = default);
    Task<RecordingTrashChange> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RecordingTrashChange> RestoreAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class RecordingDeletedException(Guid id)
    : IOException($"Recording {id:N} is in local trash. Restore it before saving or opening it.");

public sealed partial class RecordingLibraryStore
{
    private string TrashDirectory => Path.Combine(_directory, ".trash");
    private string TrashPath(Guid id) => Path.Combine(TrashDirectory, Path.GetFileName(RecordPath(id)));
    private void ThrowIfDeleted(Guid id)
    {
        if (File.Exists(TrashPath(id))) throw new RecordingDeletedException(id);
    }

    public async Task<RecordingTrashRead> ListTrashAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(TrashDirectory)) return new([], []);
            var items = new List<DeletedRecording>();
            var warnings = new List<string>();
            foreach (var path in Directory.EnumerateFiles(TrashDirectory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
                try
                {
                    var document = await ReadTrashAsync(id, cancellationToken).ConfigureAwait(false);
                    items.Add(new(document.Metadata, document.DeletedAt));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { warnings.Add($"Could not read trash entry {id:N}: {error.Message}. Its files have been retained."); }
            }
            return new(items.OrderByDescending(item => item.DeletedAt).ToArray(), warnings);
        }
        finally { _gate.Release(); }
    }

    public async Task<RecordingTrashChange> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDeleted(id);
            var path = RecordPath(id);
            var primary = await ReadCopyAsync(path, cancellationToken).ConfigureAwait(false);
            var backup = await ReadCopyAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
            var metadata = ReadRetainedRecording(id, primary, backup).Metadata;
            var document = new TrashDocument(1, metadata, DateTimeOffset.UtcNow, primary, backup);
            Directory.CreateDirectory(TrashDirectory);
            // Publishing this durable bundle is the deletion commit. Even if removing a
            // live duplicate fails or the process exits, neither copy can resurrect it.
            await AtomicFile.WriteAsync(TrashPath(id), JsonSerializer.SerializeToUtf8Bytes(document),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var warnings = new List<string>();
            foreach (var copy in new[] { path, path + ".bak" })
            {
                try { File.Delete(copy); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { warnings.Add($"{metadata.Name} is safely in local trash; a redundant saved copy could not be removed: {error.Message}"); }
            }
            return new(metadata, warnings);
        }
        finally { _gate.Release(); }
    }

    public async Task<RecordingTrashChange> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadTrashAsync(id, cancellationToken).ConfigureAwait(false);
            var path = RecordPath(id);
            await RestoreCopyAsync(path, document.Primary, cancellationToken).ConfigureAwait(false);
            await RestoreCopyAsync(path + ".bak", document.Backup, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // The marker remains until BOTH saved copies are restored. Failed or
            // interrupted restores remain hidden and can safely be retried.
            File.Delete(TrashPath(id));
            return new(document.Metadata, []);
        }
        finally { _gate.Release(); }
    }

    private static async Task RestoreCopyAsync(string path, byte[]? bytes, CancellationToken cancellationToken)
    {
        if (bytes is null) File.Delete(path);
        else await AtomicFile.WriteAsync(path, bytes, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadCopyAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumMacroBytes * 2L)
            throw new InvalidDataException("The saved copy exceeds the library size limit. It has been retained.");
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TrashDocument> ReadTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = TrashPath(id);
        if (new FileInfo(path).Length > MaximumMacroBytes * 6L)
            throw new InvalidDataException("The trash entry exceeds the library size limit.");
        var document = JsonSerializer.Deserialize<TrashDocument>(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException("Missing trash document.");
        if (document.Schema != 1 || document.Metadata?.Id != id
            || ReadRetainedRecording(id, document.Primary, document.Backup).Metadata != document.Metadata)
            throw new InvalidDataException("Trash identity, version, or integrity check failed.");
        return document;
    }

    private static StoredRecording ReadRetainedRecording(Guid id, byte[]? primary, byte[]? backup)
    {
        try { return ReadDocument(primary ?? throw new IOException("Missing current copy."), id); }
        catch (Exception primaryError) when (primaryError is IOException or JsonException or ArgumentException or Google.Protobuf.InvalidProtocolBufferException)
        {
            try { return ReadDocument(backup ?? throw new IOException("Missing previous copy."), id) with { Recovered = true }; }
            catch (Exception backupError) when (backupError is IOException or JsonException or ArgumentException or Google.Protobuf.InvalidProtocolBufferException)
            { throw new IOException("Neither saved copy can be read. The files have been retained.", new AggregateException(primaryError, backupError)); }
        }
    }

    private sealed record TrashDocument(int Schema, RecordingLibraryItem Metadata, DateTimeOffset DeletedAt, byte[]? Primary, byte[]? Backup);
}
