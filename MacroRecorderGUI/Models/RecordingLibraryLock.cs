namespace MacroRecorderGUI.Models;

internal interface IRecordingLibraryLock
{
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken);
}

/// <summary>An OS-owned file handle serializes all instances that use this library.</summary>
internal sealed class RecordingLibraryLock(string directory) : IRecordingLibraryLock
{
    internal const string FileName = ".library.lock";
    public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream lease;
            try
            {
                // Never delete or replace this file: all contenders must lock the
                // same file identity. The OS releases the handle on process exit.
                lease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                // Only sharing/lock violations are contention. Permission, disk,
                // and path errors propagate instead of becoming an endless retry.
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (!cancellationToken.IsCancellationRequested) return (IDisposable)lease;
            lease.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }, cancellationToken);
}
