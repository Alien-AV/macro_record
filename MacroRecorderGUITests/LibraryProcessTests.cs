using System.Diagnostics;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class LibraryProcessTests
{
    private DirectoryInfo _temporary = null!;
    private string Root => Path.Combine(_temporary.FullName, "library");
    private RecordingLibraryStore Store => new(Root);
    [TestInitialize] public void Initialize() => _temporary = Directory.CreateTempSubdirectory("macro-process-library-");
    [TestCleanup] public void Cleanup() => _temporary.Delete(recursive: true);
    private string RecordPath(Guid id) => Path.Combine(Root, $"{id:N}.json");
    private static StoredRecording Record(Guid id, int revision)
    {
        var bytes = SerializeEvents.SerializeEventsToByteArray([new KeyboardEvent(VirtualKey.B, false) { TimeSinceLastEvent = (ulong)revision }])
            .Concat(new byte[] { 0xA0, 0x06, (byte)revision }).ToArray();
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(revision);
        return new(RecordingLibraryStore.Describe(id, $"Revision {revision}", false, time, time, bytes), bytes);
    }

    private async Task<Guid> SeedAsync()
    {
        var id = Guid.NewGuid();
        await Store.SaveAsync(Record(id, 1));
        await Store.SaveAsync(Record(id, 2));
        return id;
    }

    [TestMethod]
    public async Task SeparateProcessSaveThenDeleteArchivesTheSuccessfulLatestSaveAndItsBackup()
    {
        var id = await SeedAsync();
        await using var save = Start("save", id);
        await save.ExpectAsync("UNCONTENDED"); await save.ExpectAsync("LOCKED");
        await using var delete = Start("delete", id);
        await delete.ExpectAsync("CONTENDING");
        await save.SendAsync("continue"); await save.ExpectAsync("DONE");
        await delete.ExpectAsync("LOCKED");
        // The deleting process now owns the lease but is paused before its snapshot.
        var latest = await File.ReadAllBytesAsync(RecordPath(id));
        var backup = await File.ReadAllBytesAsync(RecordPath(id) + ".bak");
        await delete.SendAsync("continue"); await delete.ExpectAsync("DONE");
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        await Store.RestoreAsync(id);
        CollectionAssert.AreEqual(latest, await File.ReadAllBytesAsync(RecordPath(id)));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(RecordPath(id) + ".bak"));
        CollectionAssert.AreEqual(Record(id, 3).MacroBytes, (await Store.LoadAsync(id)).MacroBytes);
    }

    [TestMethod]
    public async Task SeparateProcessDeleteThenSaveRejectsTheSaveAndRetainsBothDeletedCopies()
    {
        var id = await SeedAsync();
        var primary = await File.ReadAllBytesAsync(RecordPath(id));
        var backup = await File.ReadAllBytesAsync(RecordPath(id) + ".bak");
        await using var delete = Start("delete", id);
        await delete.ExpectAsync("UNCONTENDED"); await delete.ExpectAsync("LOCKED");
        await using var save = Start("save", id);
        await save.ExpectAsync("CONTENDING");
        await delete.SendAsync("continue"); await delete.ExpectAsync("DONE");
        await save.ExpectAsync("LOCKED"); await save.SendAsync("continue"); await save.ExpectAsync("DELETED");
        // A failed operation must release its lease while the helper remains alive.
        await Store.RestoreAsync(id).WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(primary, await File.ReadAllBytesAsync(RecordPath(id)));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(RecordPath(id) + ".bak"));
    }

    [TestMethod]
    public async Task SeparateProcessRestoreThenSaveKeepsSuccessfulNewBytesAndRestoredPrimaryAsBackup()
    {
        var id = await SeedAsync();
        var restoredPrimary = await File.ReadAllBytesAsync(RecordPath(id));
        await Store.DeleteAsync(id);
        await using var restore = Start("restore", id);
        await restore.ExpectAsync("UNCONTENDED"); await restore.ExpectAsync("LOCKED");
        await using var save = Start("save", id);
        await save.ExpectAsync("CONTENDING");
        await restore.SendAsync("continue"); await restore.ExpectAsync("DONE");
        await save.ExpectAsync("LOCKED"); await save.SendAsync("continue"); await save.ExpectAsync("DONE");
        CollectionAssert.AreEqual(Record(id, 3).MacroBytes, (await Store.LoadAsync(id)).MacroBytes);
        CollectionAssert.AreEqual(restoredPrimary, await File.ReadAllBytesAsync(RecordPath(id) + ".bak"));
        Assert.AreEqual(0, (await Store.ListTrashAsync()).Items.Count);
    }

    [TestMethod]
    public async Task SeparateProcessSaveBeforeRestoreIsRejectedWithoutOverwritingRecoverableBytes()
    {
        var id = await SeedAsync();
        var primary = await File.ReadAllBytesAsync(RecordPath(id));
        var backup = await File.ReadAllBytesAsync(RecordPath(id) + ".bak");
        await Store.DeleteAsync(id);
        await using var save = Start("save", id);
        await save.ExpectAsync("UNCONTENDED"); await save.ExpectAsync("LOCKED");
        await using var restore = Start("restore", id);
        await restore.ExpectAsync("CONTENDING");
        await save.SendAsync("continue"); await save.ExpectAsync("DELETED");
        await restore.ExpectAsync("LOCKED"); await restore.SendAsync("continue"); await restore.ExpectAsync("DONE");
        CollectionAssert.AreEqual(primary, await File.ReadAllBytesAsync(RecordPath(id)));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(RecordPath(id) + ".bak"));
    }

    [TestMethod]
    [DataRow("load")]
    [DataRow("list")]
    [DataRow("trash")]
    public async Task SeparateProcessReadsWaitForRestoreCommit(string operation)
    {
        var id = await SeedAsync(); await Store.DeleteAsync(id);
        await using var restore = Start("restore", id);
        await restore.ExpectAsync("UNCONTENDED"); await restore.ExpectAsync("LOCKED");
        await using var read = Start(operation, id);
        await read.ExpectAsync("CONTENDING");
        await restore.SendAsync("continue"); await restore.ExpectAsync("DONE");
        await read.ExpectAsync("LOCKED"); await read.SendAsync("continue"); await read.ExpectAsync("DONE");
    }

    [TestMethod]
    [DataRow("save")]
    [DataRow("load")]
    [DataRow("delete")]
    [DataRow("restore")]
    [DataRow("list")]
    [DataRow("trash")]
    public async Task WaitingOperationsCancelWithoutTakingOrLeakingTheOtherProcessLease(string operation)
    {
        var id = await SeedAsync();
        await using var owner = Start("load", id);
        await owner.ExpectAsync("UNCONTENDED"); await owner.ExpectAsync("LOCKED");
        await using var waiter = Start(operation, id);
        await waiter.ExpectAsync("CONTENDING");
        await waiter.SendAsync("cancel"); await waiter.ExpectAsync("CANCELLED");
        await owner.SendAsync("continue"); await owner.ExpectAsync("DONE");
        CollectionAssert.AreEqual(Record(id, 2).MacroBytes, (await Store.LoadAsync(id).WaitAsync(TimeSpan.FromSeconds(5))).MacroBytes);
    }

    [TestMethod]
    public async Task CancellationAfterAcquisitionReleasesLeaseWithoutChangingEitherCopy()
    {
        var id = await SeedAsync();
        var primary = await File.ReadAllBytesAsync(RecordPath(id));
        var backup = await File.ReadAllBytesAsync(RecordPath(id) + ".bak");
        await using var owner = Start("delete", id);
        await owner.ExpectAsync("UNCONTENDED"); await owner.ExpectAsync("LOCKED");
        await owner.SendAsync("cancel"); await owner.ExpectAsync("CANCELLED");
        await Store.LoadAsync(id).WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(primary, await File.ReadAllBytesAsync(RecordPath(id)));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(RecordPath(id) + ".bak"));
    }

    [TestMethod]
    public async Task ExitingProcessReleasesLeaseAndAnotherLibraryDoesNotWaitForIt()
    {
        var id = await SeedAsync();
        await using var owner = Start("load", id);
        await owner.ExpectAsync("UNCONTENDED"); await owner.ExpectAsync("LOCKED");
        var other = new RecordingLibraryStore(Path.Combine(_temporary.FullName, "other-library"));
        await other.SaveAsync(Record(Guid.NewGuid(), 4)).WaitAsync(TimeSpan.FromSeconds(5));
        await using var waiter = Start("save", id);
        await waiter.ExpectAsync("CONTENDING");
        await owner.KillAsync();
        await waiter.ExpectAsync("LOCKED"); await waiter.SendAsync("continue"); await waiter.ExpectAsync("DONE");
        CollectionAssert.AreEqual(Record(id, 3).MacroBytes, (await Store.LoadAsync(id)).MacroBytes);
    }

    [TestMethod]
    public async Task InvalidLockPathFailsPromptlyInsteadOfRetryingAsContention()
    {
        Directory.CreateDirectory(Path.Combine(Root, RecordingLibraryLock.FileName));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Store.SaveAsync(Record(Guid.NewGuid(), 1)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private Child Start(string operation, Guid id) => new(Root, operation, id);

    // Console-only entry point: no WinUI application, engines, windows, or user paths.
    internal static async Task<int> RunChildAsync(string[] args)
    {
        if (args is not ["--library-process-check", var directory, var operation, var textId] || !Guid.TryParse(textId, out var id)) return 2;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is { } command)
            {
                if (command == "continue") resume.TrySetResult();
                else if (command == "cancel") cancellation.Cancel();
                else if (command == "exit") { exit.TrySetResult(); break; }
            }
        });
        var store = new RecordingLibraryStore(directory, new PausedProcessLock(directory, resume.Task));
        try
        {
            switch (operation)
            {
                case "save": await store.SaveAsync(Record(id, 3), cancellation.Token); break;
                case "delete": await store.DeleteAsync(id, cancellation.Token); break;
                case "restore": await store.RestoreAsync(id, cancellation.Token); break;
                case "load": await store.LoadAsync(id, cancellation.Token); break;
                case "list": await store.ListAsync(cancellation.Token); break;
                case "trash": await store.ListTrashAsync(cancellation.Token); break;
                default: return 2;
            }
            Console.WriteLine("DONE");
        }
        catch (RecordingDeletedException) { Console.WriteLine("DELETED"); }
        catch (OperationCanceledException) { Console.WriteLine("CANCELLED"); }
        catch (Exception error) { Console.Error.WriteLine(error); Console.WriteLine("ERROR"); return 1; }
        await exit.Task.WaitAsync(TimeSpan.FromSeconds(20));
        return 0;
    }

    private sealed class PausedProcessLock(string directory, Task resume) : IRecordingLibraryLock
    {
        public async Task<IDisposable> AcquireAsync(CancellationToken token)
        {
            // Probe the real OS handle to acknowledge contention deterministically,
            // rather than depending on sleeps or the order of process scheduling.
            try
            {
                using var probe = new FileStream(Path.Combine(directory, RecordingLibraryLock.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Console.WriteLine("UNCONTENDED");
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { Console.WriteLine("CONTENDING"); }
            var lease = await new RecordingLibraryLock(directory).AcquireAsync(token);
            try
            {
                Console.WriteLine("LOCKED");
                await resume.WaitAsync(token);
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }
    }

    private sealed class Child : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _errors;
        public Child(string directory, string operation, Guid id)
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "MacroRecorderGUITests.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "--library-process-check", directory, operation, id.ToString() }) start.ArgumentList.Add(argument);
            _process = Process.Start(start)!;
            _errors = _process.StandardError.ReadToEndAsync();
        }
        public async Task ExpectAsync(string expected)
        {
            var actual = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(expected, actual, _process.HasExited ? await _errors : "Child process protocol mismatch.");
        }
        public async Task SendAsync(string command)
        {
            await _process.StandardInput.WriteLineAsync(command); await _process.StandardInput.FlushAsync();
        }
        public async Task KillAsync()
        {
            if (!_process.HasExited) _process.Kill();
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    await SendAsync("exit");
                    try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { await KillAsync(); }
                }
            }
            finally { _process.Dispose(); }
        }
    }
}
