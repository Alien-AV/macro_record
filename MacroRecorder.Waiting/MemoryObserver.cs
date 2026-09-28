using System.Buffers.Binary;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

public sealed record ProcessChoice(uint ProcessId, ulong ProcessCreated, string ExecutablePath)
{
    public string Identity => $"{ProcessId}:{ProcessCreated}:{ExecutablePath}";
}
public sealed record ModuleChoice(string Path, string FileVersion, ulong BaseAddress, uint Size);
public sealed record ChoiceResult<T>(ReadStatus Status, IReadOnlyList<T> Choices, string Detail = "", bool IsComplete = true);
internal sealed record MemoryReadResult(bool Success, byte[] Bytes, int BytesRead, string Detail = "");
internal interface IMemoryProcess : IDisposable
{
    ProcessChoice Identity { get; }
    int PointerSize { get; }
    bool IsAlive { get; }
    IReadOnlyList<ModuleChoice> Modules(CancellationToken token);
    MemoryReadResult Read(ulong address, int count);
}
internal interface IMemoryProcessApi
{
    IMemoryProcess? Bind(string executablePath, CancellationToken token);
}

internal sealed class MemoryObserver(IMemoryProcessApi api, Func<bool> permitted) : IWaitObserver, IAsyncDisposable
{
    private IMemoryProcess? _process;
    private ModuleChoice? _module;
    private bool _disposed;

    public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken cancellationToken)
    {
        try { return ValueTask.FromResult(Observe(condition.Memory ?? throw new ArgumentException("Memory condition is required."), cancellationToken)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return ValueTask.FromResult(new WaitObservation(ObservationState.Error, error.Message)); }
    }

    private WaitObservation Observe(MemoryCondition memory, CancellationToken token)
    {
        Check(token);
        _process ??= api.Bind(memory.ExecutablePath, token);
        if (_process is null) return new(ObservationState.Unavailable, "Target process has not started.");
        EnsureAlive(token);
        var pointerSize = _process.PointerSize;
        if (pointerSize is not (4 or 8)) throw new InvalidOperationException("Target pointer width is unsupported.");
        ulong address;
        if (memory.Module is { } requested)
        {
            var modules = _process.Modules(token).Where(m => string.Equals(m.Path, requested.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(token);
            if (modules.Length != 1) return new(ObservationState.Unavailable, "The configured module is missing or ambiguous.");
            var module = modules[0];
            if (module.FileVersion != requested.FileVersion) throw new InvalidOperationException("Module file version differs from the configured identity.");
            _module ??= module;
            if (_module != module) throw new InvalidOperationException("The bound module changed during this occurrence.");
            if (requested.Offset >= module.Size) throw new InvalidOperationException("Module-relative offset is outside the module image.");
            var initialWidth = memory.PointerOffsets.Count > 0 ? pointerSize : ScalarValue.Width(memory.ScalarType);
            if ((ulong)initialWidth > module.Size - requested.Offset) throw new InvalidOperationException("The initial read crosses the module image boundary.");
            address = checked(module.BaseAddress + requested.Offset);
        }
        else address = memory.AbsoluteAddress;

        // Re-resolve every explicit step on every sample. This is not an atomic
        // multi-field snapshot: the target can mutate between the bounded reads.
        foreach (var offset in memory.PointerOffsets)
        {
            var pointer = ReadExact(address, pointerSize, pointerSize, token);
            if (pointer is null) return new(ObservationState.Unavailable, "A pointer read failed or was partial.");
            address = pointerSize == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(pointer) : BinaryPrimitives.ReadUInt64LittleEndian(pointer);
            if (address == 0) return new(ObservationState.Unavailable, "The pointer chain contains a null pointer.");
            address = offset >= 0 ? checked(address + (ulong)offset) : checked(address - ((ulong)(-(offset + 1)) + 1));
        }
        var bytes = ReadExact(address, ScalarValue.Width(memory.ScalarType), pointerSize, token);
        if (bytes is null) return new(ObservationState.Unavailable, "The scalar read failed or was partial.");
        EnsureAlive(token);
        ScalarValue scalar;
        try { scalar = ScalarValue.Decode(memory.ScalarType, bytes); }
        catch (ArgumentException) { return new(ObservationState.Unavailable, "Scalar is nonfinite or could not be interpreted completely."); }
        var match = scalar.Matches(ScalarValue.Parse(memory.ScalarType, memory.Expected), memory.Comparison, memory.Tolerance);
        // The logical selector belongs to the immutable occurrence definition.
        // The allocation currently reached by a pointer chain is not identity.
        return new(match ? ObservationState.Match : ObservationState.NoMatch,
            match ? "Memory condition matched." : "Memory condition did not match.", _process.Identity.Identity, Scalar: scalar);
    }

    private byte[]? ReadExact(ulong address, int count, int pointerSize, CancellationToken token)
    {
        EnsureAlive(token);
        var max = pointerSize == 4 ? uint.MaxValue : ulong.MaxValue;
        if (address == 0 || address > max || (ulong)(count - 1) > max - address)
            throw new InvalidOperationException("Memory address or read range exceeds the target pointer width.");
        var result = _process!.Read(address, count);
        EnsureAlive(token);
        return result.Success && result.BytesRead == count && result.Bytes.Length == count ? result.Bytes : null;
    }
    private void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!permitted()) throw new InvalidOperationException("Read-only memory observation is disabled in local settings.");
    }
    private void EnsureAlive(CancellationToken token)
    {
        Check(token);
        if (!_process!.IsAlive) throw new InvalidOperationException("The bound process exited or its identity is no longer verifiable. This wait will not attach to a replacement.");
    }
    public ValueTask DisposeAsync()
    {
        if (!_disposed) { _disposed = true; _process?.Dispose(); }
        return ValueTask.CompletedTask;
    }
}
