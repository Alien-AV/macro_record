using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Legacy = MacroRecorderGUITests.LegacyWaitFormat;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitDocumentRecoveryTests
{
    private DirectoryInfo _directory = null!;
    [TestInitialize] public void Initialize() => _directory = Directory.CreateTempSubdirectory("macro-wait-format-tests-");
    [TestCleanup] public void Cleanup() => _directory.Delete(recursive: true);
    private string Root => Path.Combine(_directory.FullName, "library");
    private RecordingLibraryStore Store() => new(Root);
    private MainWindowViewModel ViewModel() => new(new FakeRecordEngine(), new FakePlaybackEngine(), Store());

    private static WaitCondition Condition(bool pixel = false)
    {
        var condition = WaitValidation.NewWindow();
        condition.Window.Target.Title = "Export complete";
        if (pixel) condition.Pixel = new() { Rgb = 0x123456, X = -10, Y = 20 };
        return condition;
    }

    private static byte[] Unknown(byte[] bytes, byte value = 7) => [.. bytes, 0xa0, 0x06, value];

    private static byte[] Wire(WaitCondition condition, bool mixed = false)
    {
        using var entry = new MemoryStream();
        using (var output = new CodedOutputStream(entry, leaveOpen: true))
        {
            output.WriteTag(1, WireFormat.WireType.Varint);
            output.WriteUInt64(123);
            if (mixed)
            {
                // Emit both oneof fields in order: the current parser sees the wait,
                // but the frozen old parser retains and would play the keyboard entry.
                output.WriteTag(2, WireFormat.WireType.LengthDelimited);
                output.WriteMessage(new ProtobufInputEvent.Types.KeyboardEventType { VirtualKeyCode = 0x41 });
            }
            output.WriteTag(4, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(condition);
        }
        using var list = new MemoryStream();
        using (var output = new CodedOutputStream(list, leaveOpen: true))
        {
            output.WriteTag(1, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(Unknown(entry.ToArray())));
        }
        return Unknown(list.ToArray(), 9);
    }

    private static byte[] Envelope(byte[] wire, int version = 3, byte[]? recovery = null) =>
        [.. "\0MACRO2\n"u8, .. JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = version, Events = wire, Origins = Array.Empty<PointerOriginBoundary>(),
            BeforeOriginAdoption = recovery, FutureDocumentField = new { Value = "retain exactly" }
        }, new JsonSerializerOptions { WriteIndented = true })];

    private static StoredRecording Record(byte[] bytes, Guid? id = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new(RecordingLibraryStore.Describe(id ?? Guid.NewGuid(), "Wait format", false, now, now, bytes), bytes);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public async Task RawAndVersionTwoWaitsAreRejectedBeforeImportOrOriginalByteCaching(int version, bool mixed)
    {
        var wire = Wire(Condition(), mixed);
        var current = ProtobufInputEventList.Parser.ParseFrom(wire).InputEvents.Single();
        Assert.AreEqual(ProtobufInputEvent.EventOneofCase.WaitCondition, current.EventCase);
        var old = Legacy.ProtobufInputEventList.Parser.ParseFrom(wire).InputEvents.Single();
        Assert.IsNull(Legacy.ProtobufInputEvent.Descriptor.FindFieldByNumber(4));
        Assert.AreEqual(mixed ? Legacy.ProtobufInputEvent.EventOneofCase.KeyboardEvent
            : Legacy.ProtobufInputEvent.EventOneofCase.None, old.EventCase);
        if (mixed) Assert.AreEqual(0x41u, old.KeyboardEvent.VirtualKeyCode);

        var bytes = version == 1 ? wire : Envelope(wire, version);
        var error = Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(bytes));
        StringAssert.Contains(error.Message, "version 3");
        Assert.Throws<InvalidDataException>(() => Record(bytes));
        using var restored = new MacroViewModel("Unimported", new FakePlaybackEngine());
        var metadata = Record(Envelope(wire)).Metadata;
        Assert.Throws<InvalidDataException>(() => restored.Restore(new(metadata, bytes)));
        Assert.AreEqual(0, restored.Events.Count);

        var source = Path.Combine(_directory.FullName, "unsafe.macro");
        await File.WriteAllBytesAsync(source, bytes);
        using var vm = ViewModel();
        var active = vm.ActiveMacro;
        var tabs = vm.MacroTabs.Count;
        await Assert.ThrowsAsync<InvalidDataException>(() => vm.ImportRecordingAsync(source));
        Assert.AreSame(active, vm.ActiveMacro);
        Assert.AreEqual(tabs, vm.MacroTabs.Count);
        Assert.AreEqual(0, (await Store().ListAsync()).Items.Count);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task UntouchedVersionThreeImportSaveAndExportPreserveAllBytesAndUnknownFields(bool pixel, bool mixed)
    {
        var condition = Condition(pixel);
        if (!pixel)
        {
            condition.Window.Target = WindowSelector.Parser.ParseFrom(Unknown(condition.Window.Target.ToByteArray()));
            condition.Window = WindowCondition.Parser.ParseFrom(Unknown(condition.Window.ToByteArray(), 8));
        }
        else condition.Pixel = PixelCondition.Parser.ParseFrom(Unknown(condition.Pixel.ToByteArray(), 8));
        condition = WaitCondition.Parser.ParseFrom(Unknown(condition.ToByteArray(), 10));
        var wire = Wire(condition, mixed);
        var bytes = Envelope(wire);
        var source = Path.Combine(_directory.FullName, "wait.macro");
        var export = Path.Combine(_directory.FullName, "export.macro");
        await File.WriteAllBytesAsync(source, bytes);
        using var vm = ViewModel();
        var macro = await vm.ImportRecordingAsync(source);
        Assert.AreEqual(1, macro.Events.Count);
        Assert.IsInstanceOfType<WaitConditionEvent>(macro.Events[0]);
        Assert.AreEqual(1, vm.Library.Single().ConditionalWaitCount);
        Assert.IsFalse(vm.Library.Single().HasKeyboard);
        macro.Name = "Renamed";
        await vm.SaveRecordingAsync(macro);
        await vm.ExportRecordingAsync(macro, export);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(export));
        CollectionAssert.AreEqual(bytes, (await Store().LoadAsync(macro.RecordingId)).MacroBytes);
        var document = RecordingDocument.Read(bytes);
        Assert.AreEqual(3, document.Version);
        CollectionAssert.AreEqual(wire, document.Events);
        CollectionAssert.AreEqual(wire, RecordingDocument.Read(document.Write()).Events);
        Assert.Throws<InvalidProtocolBufferException>(() => Legacy.ProtobufInputEventList.Parser.ParseFrom(bytes));
        Assert.Throws<InvalidOperationException>(() => document.ExportLegacy());

        macro.Events[0].TimeSinceLastEvent = 456;
        var edited = RecordingDocument.Read(macro.SnapshotBytes());
        var expected = ProtobufInputEventList.Parser.ParseFrom(wire);
        expected.InputEvents[0].TimeSinceLastEvent = 456;
        CollectionAssert.AreEqual(expected.ToByteArray(), edited.Events);
        Assert.IsTrue(JsonElement.DeepEquals(document.Extensions!["FutureDocumentField"],
            edited.Extensions!["FutureDocumentField"]));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task WaitlessLegacyDocumentsRemainByteExact(int version)
    {
        byte[] wire = [0x0a, 0x06, 0x12, 0x04, 0x08, 0x41, 0x10, 0x01, 0xa0, 0x06, 0x07];
        var bytes = version == 1 ? wire : Envelope(wire, version);
        var source = Path.Combine(_directory.FullName, "legacy.macro");
        var export = Path.Combine(_directory.FullName, "export.macro");
        await File.WriteAllBytesAsync(source, bytes);
        using var vm = ViewModel();
        var macro = await vm.ImportRecordingAsync(source);
        await vm.ExportRecordingAsync(macro, export);
        CollectionAssert.AreEqual(bytes, macro.SnapshotBytes());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(export));
        CollectionAssert.AreEqual(wire, RecordingDocument.Read(bytes).ExportLegacy());
    }

    [TestMethod]
    public void NewWaitSavesRequireVersionThreeWhileNativeTransportStaysRaw()
    {
        var condition = Condition();
        var input = new WaitConditionEvent(condition);
        var raw = SerializeEvents.SerializeEventsToByteArray([input]);
        Assert.AreEqual(condition, ProtobufInputEventList.Parser.ParseFrom(raw).InputEvents.Single().WaitCondition);
        Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(raw));
        var bytes = new RecordingDocument { Events = raw }.Write();
        Assert.AreEqual(3, RecordingDocument.Read(bytes).Version);
        CollectionAssert.AreEqual(raw, RecordingDocument.Read(bytes).Events);
        Assert.Throws<InvalidProtocolBufferException>(() => Legacy.ProtobufInputEventList.Parser.ParseFrom(bytes));
    }

    private static WaitCondition Malformed(string kind)
    {
        var condition = Condition();
        switch (kind)
        {
            case "semantics": condition.SemanticsVersion = 2; break;
            case "provider": condition.ClearCondition(); break;
            case "trigger": condition.Trigger = (WaitTrigger)99; break;
            case "timeout": condition.TimeoutUs = 0; break;
            case "maximum timeout": condition.TimeoutUs = ulong.MaxValue; break;
            case "stability": condition.StableForUs = condition.TimeoutUs + 1; break;
            case "poll": condition.PollIntervalUs = 1; break;
            case "maximum poll": condition.PollIntervalUs = ulong.MaxValue; break;
            case "selector": condition.Window.Target = null; break;
            case "empty selector": condition.Window.Target.Title = ""; break;
            case "selector bounds": condition.Window.Target.Title = new string('x', 1025); break;
            case "window test": condition.Window.Test = (WindowTest)99; break;
            case "pixel bounds": condition.Pixel = new() { Rgb = 0x1000000 }; break;
            case "pixel coordinates": condition.Pixel = new() { Coordinates = (PixelCoordinates)99 }; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return condition;
    }

    [TestMethod]
    [DataRow("semantics")]
    [DataRow("provider")]
    [DataRow("trigger")]
    [DataRow("timeout")]
    [DataRow("maximum timeout")]
    [DataRow("stability")]
    [DataRow("poll")]
    [DataRow("maximum poll")]
    [DataRow("selector")]
    [DataRow("empty selector")]
    [DataRow("selector bounds")]
    [DataRow("window test")]
    [DataRow("pixel bounds")]
    [DataRow("pixel coordinates")]
    public async Task MalformedSavedWaitsAreRejectedWithoutMutatingExistingLibraryCopies(string kind)
    {
        var healthy = Record(Envelope(Wire(Condition())));
        var store = Store();
        await store.SaveAsync(healthy);
        await store.SaveAsync(healthy);
        var path = Path.Combine(Root, $"{healthy.Metadata.Id:N}.json");
        var primary = await File.ReadAllBytesAsync(path);
        var backup = await File.ReadAllBytesAsync(path + ".bak");
        var wire = Wire(Malformed(kind));
        var bytes = Envelope(wire);
        var validation = Assert.Throws<ArgumentException>(() => WaitValidation.Validate(Malformed(kind)));
        var error = Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(bytes));
        StringAssert.Contains(error.Message, "event 1");
        StringAssert.Contains(error.Message, validation.Message);
        Assert.Throws<InvalidDataException>(() => new RecordingDocument { Events = wire }.Write());
        Assert.Throws<InvalidDataException>(() => Record(bytes));
        Assert.Throws<InvalidDataException>(() => SerializeEvents.DeserializeEventsFromByteArray(bytes).ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(healthy with { MacroBytes = bytes }));

        using var restored = new MacroViewModel("Unimported", new FakePlaybackEngine());
        Assert.Throws<InvalidDataException>(() => restored.Restore(healthy with { MacroBytes = bytes }));
        Assert.AreEqual(0, restored.Events.Count);
        using var vm = ViewModel();
        await vm.InitializeLibraryAsync();
        var active = vm.ActiveMacro;
        var tabs = vm.MacroTabs.Count;
        var source = Path.Combine(_directory.FullName, "malformed.macro");
        await File.WriteAllBytesAsync(source, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => vm.ImportRecordingAsync(source));
        Assert.AreSame(active, vm.ActiveMacro);
        Assert.AreEqual(tabs, vm.MacroTabs.Count);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source));
        CollectionAssert.AreEqual(primary, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(path + ".bak"));
        Assert.AreEqual(healthy.Metadata, (await store.ListAsync()).Items.Single());
        Assert.AreEqual(0, Directory.GetFiles(Root, "*.tmp").Length);
    }

    [TestMethod]
    public void IncompleteEditorDraftCanBeConstructedAndCompletedBeforeSaving()
    {
        var draft = new WaitConditionEvent(WaitValidation.NewWindow());
        using var macro = new MacroViewModel("Draft", new FakePlaybackEngine());
        macro.AddEvent(draft);
        Assert.AreEqual("", draft.Condition.Window.Target.Title);
        Assert.Throws<InvalidDataException>(() => macro.SnapshotBytes());
        var completed = draft.Condition;
        completed.Window.Target.Title = "Ready";
        draft.SetCondition(completed);
        Assert.AreEqual(completed, RecordingDocument.Read(macro.SnapshotBytes()).ParseEvents().InputEvents.Single().WaitCondition);
    }

    private async Task<(StoredRecording Healthy, string Path, byte[] Primary, byte[] Backup)> CorruptPrimary(string kind)
    {
        var healthy = Record(Envelope(Wire(Condition())));
        var store = Store();
        await store.SaveAsync(healthy);
        await store.SaveAsync(healthy);
        var path = Path.Combine(Root, $"{healthy.Metadata.Id:N}.json");
        var document = JsonNode.Parse(await File.ReadAllBytesAsync(path))!;
        var bad = Envelope(Wire(Malformed(kind)));
        document["MacroBytes"] = Convert.ToBase64String(bad);
        document["Sha256"] = Convert.ToHexString(SHA256.HashData(bad));
        var primary = JsonSerializer.SerializeToUtf8Bytes(document);
        await File.WriteAllBytesAsync(path, primary);
        return (healthy, path, primary, await File.ReadAllBytesAsync(path + ".bak"));
    }

    [TestMethod]
    [DataRow("semantics")]
    [DataRow("provider")]
    [DataRow("timeout")]
    public async Task ChecksumValidMalformedPrimaryFallsBackAndRepairPreservesHealthyBackup(string kind)
    {
        var fixture = await CorruptPrimary(kind);
        var store = Store();
        var recovered = await store.LoadAsync(fixture.Healthy.Metadata.Id);
        Assert.IsTrue(recovered.Recovered);
        Assert.AreEqual(fixture.Healthy.Metadata, recovered.Metadata);
        CollectionAssert.AreEqual(fixture.Healthy.MacroBytes, recovered.MacroBytes);
        var listing = await store.ListAsync();
        Assert.AreEqual(fixture.Healthy.Metadata, listing.Items.Single());
        StringAssert.Contains(listing.Warnings.Single(), "previous saved copy");
        CollectionAssert.AreEqual(fixture.Primary, await File.ReadAllBytesAsync(fixture.Path));
        CollectionAssert.AreEqual(fixture.Backup, await File.ReadAllBytesAsync(fixture.Path + ".bak"));

        await store.SaveAsync(recovered);
        Assert.IsFalse((await store.LoadAsync(recovered.Metadata.Id)).Recovered);
        CollectionAssert.AreEqual(fixture.Backup, await File.ReadAllBytesAsync(fixture.Path + ".bak"));
        await File.WriteAllBytesAsync(fixture.Path, fixture.Primary);
        CollectionAssert.AreEqual(fixture.Healthy.MacroBytes, (await store.LoadAsync(recovered.Metadata.Id)).MacroBytes);
    }

    [TestMethod]
    [DataRow("semantics")]
    [DataRow("provider")]
    [DataRow("timeout")]
    public async Task TrashRetainsAndRestoresExactMalformedPrimaryAndHealthyBackup(string kind)
    {
        var fixture = await CorruptPrimary(kind);
        var store = Store();
        var id = fixture.Healthy.Metadata.Id;
        var deleted = await store.DeleteAsync(id);
        Assert.AreEqual(fixture.Healthy.Metadata, deleted.Metadata);
        Assert.AreEqual(0, deleted.Warnings.Count);
        Assert.IsFalse(File.Exists(fixture.Path));
        Assert.IsFalse(File.Exists(fixture.Path + ".bak"));
        var trashBytes = await File.ReadAllBytesAsync(Path.Combine(Root, ".trash", $"{id:N}.json"));
        using var trash = JsonDocument.Parse(trashBytes);
        CollectionAssert.AreEqual(fixture.Primary, trash.RootElement.GetProperty("Primary").GetBytesFromBase64());
        CollectionAssert.AreEqual(fixture.Backup, trash.RootElement.GetProperty("Backup").GetBytesFromBase64());
        var listing = await store.ListTrashAsync();
        Assert.AreEqual(fixture.Healthy.Metadata, listing.Items.Single().Metadata);
        Assert.AreEqual(0, listing.Warnings.Count);
        await Assert.ThrowsAsync<RecordingDeletedException>(() => store.SaveAsync(fixture.Healthy));
        await store.RestoreAsync(id);
        CollectionAssert.AreEqual(fixture.Primary, await File.ReadAllBytesAsync(fixture.Path));
        CollectionAssert.AreEqual(fixture.Backup, await File.ReadAllBytesAsync(fixture.Path + ".bak"));
        Assert.AreEqual(0, (await store.ListTrashAsync()).Items.Count);
        var recovered = await store.LoadAsync(id);
        Assert.IsTrue(recovered.Recovered);
        CollectionAssert.AreEqual(fixture.Healthy.MacroBytes, recovered.MacroBytes);
    }

    [TestMethod]
    public async Task WhenBothCopiesHaveInvalidWaitsTheErrorsExplainWhyAndRetainEveryFile()
    {
        var fixture = await CorruptPrimary("provider");
        await File.WriteAllBytesAsync(fixture.Path + ".bak", fixture.Primary);
        var store = Store();
        var error = await Assert.ThrowsAsync<IOException>(() => store.LoadAsync(fixture.Healthy.Metadata.Id));
        StringAssert.Contains(error.Message, "event 1");
        StringAssert.Contains(error.Message, "supported wait condition");
        Assert.IsInstanceOfType<AggregateException>(error.InnerException);
        var listing = await store.ListAsync();
        Assert.AreEqual(0, listing.Items.Count);
        StringAssert.Contains(listing.Warnings.Single(), "supported wait condition");
        await Assert.ThrowsAsync<IOException>(() => store.DeleteAsync(fixture.Healthy.Metadata.Id));
        CollectionAssert.AreEqual(fixture.Primary, await File.ReadAllBytesAsync(fixture.Path));
        CollectionAssert.AreEqual(fixture.Primary, await File.ReadAllBytesAsync(fixture.Path + ".bak"));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void RecoveryCopiesUseTheSameWaitFormatAndDefinitionValidation(int version)
    {
        var wire = Wire(version == 3 ? Malformed("provider") : Condition());
        var recovery = version == 1 ? wire : Envelope(wire, version);
        var error = Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(Envelope([], recovery: recovery)));
        StringAssert.Contains(error.Message, version == 3 ? "supported wait condition" : "version 3");
    }

    [TestMethod]
    public void WaitlessRawProtobufAtThe64MiBLimitRemainsByteExact()
    {
        var bytes = new byte[RecordingLibraryStore.MaximumMacroBytes];
        // One large unknown length-delimited field: two tag bytes and four length bytes.
        using (var output = new CodedOutputStream(bytes))
        {
            output.WriteTag(100, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(new byte[bytes.Length - 6]));
        }
        var document = RecordingDocument.Read(bytes);
        CollectionAssert.AreEqual(bytes, document.Write());
        var error = Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(new byte[bytes.Length + 1]));
        StringAssert.Contains(error.Message, "64 MB");
    }
}
