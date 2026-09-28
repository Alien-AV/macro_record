using System.Text.Json;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitCapturePreferencesTests
{
    private DirectoryInfo _directory = null!;
    private string PreferencePath => Path.Combine(_directory.FullName, "capture.json");
    [TestInitialize] public void Initialize() => _directory = Directory.CreateTempSubdirectory("macro-capture-preferences-");
    [TestCleanup] public void Cleanup() => _directory.Delete(recursive: true);

    [TestMethod]
    public async Task MissingPreferencesAreDisabledWithoutCreatingAFileAndExplicitChoicesRoundTrip()
    {
        var store = new WaitCapturePreferenceStore(PreferencePath);
        var missing = await store.LoadAsync(); Assert.AreEqual(WaitCaptureConfiguration.Default, missing.Configuration);
        Assert.IsFalse(missing.Configuration.Bindings().Any(pair => pair.Binding.Enabled)); Assert.IsFalse(File.Exists(PreferencePath));
        var changed = missing.Configuration with { HoveredWindow = new(true, VirtualKey.G, HotKeyModifiers.Control | HotKeyModifiers.Shift) };
        await store.SaveAsync(changed);
        Assert.AreEqual(changed, (await new WaitCapturePreferenceStore(PreferencePath).LoadAsync()).Configuration);
        Assert.IsNull((await store.LoadAsync()).Warning);
        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(PreferencePath));
        Assert.AreEqual(1, file.RootElement.GetProperty("Schema").GetInt32());
        Assert.IsFalse(file.RootElement.TryGetProperty("MacroBytes", out _));
        Assert.IsEmpty(Directory.GetFiles(_directory.FullName, "*.tmp"));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"Schema\":2}")]
    [DataRow("{\"Schema\":1.5}")]
    [DataRow("{\"Schema\":2147483648}")]
    [DataRow("{\"Schema\":-2147483649}")]
    [DataRow("{\"Schema\":1e100}")]
    [DataRow("{\"Schema\":1,\"Schema\":1,\"Capture\":{}}")]
    [DataRow("{\"Schema\":1,\"Capture\":{\"FocusedWindow\":{\"Enabled\":true}}}")]
    public async Task MalformedIncompleteAndUnsupportedFilesStayOffAndRecoverWithExplicitSave(string json)
    {
        await File.WriteAllTextAsync(PreferencePath, json);
        var store = new WaitCapturePreferenceStore(PreferencePath);
        var result = await store.LoadAsync();
        Assert.IsNotNull(result.Warning); Assert.AreEqual(WaitCaptureConfiguration.Default, result.Configuration);
        Assert.AreEqual(json, await File.ReadAllTextAsync(PreferencePath));
        var enabled = WaitCaptureConfiguration.Default with { PointerPixel = WaitCaptureConfiguration.Default.PointerPixel with { Enabled = true } };
        await store.SaveAsync(enabled);
        var recovered = await store.LoadAsync();
        Assert.IsNull(recovered.Warning); Assert.AreEqual(enabled, recovered.Configuration);
    }

    [TestMethod]
    public async Task InvalidReservedAndNullBindingsCannotBePersisted()
    {
        var store = new WaitCapturePreferenceStore(PreferencePath);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsync(WaitCaptureConfiguration.Default with
        { PointerPixel = new(true, VirtualKey.Q, HotKeyModifiers.Control) }));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsync(WaitCaptureConfiguration.Default with { FocusedWindow = null! }));
        Assert.IsFalse(File.Exists(PreferencePath));
    }

    [TestMethod]
    public async Task UnavailableStoreReportsDisabledReadAndWriteFailureInsteadOfPretendingSaveSucceeded()
    {
        var store = new WaitCapturePreferenceStore(_directory.FullName);
        var result = await store.LoadAsync(); Assert.IsNotNull(result.Warning);
        Assert.IsFalse(result.Configuration.Bindings().Any(pair => pair.Binding.Enabled));
        await Assert.ThrowsAsync<Exception>(() => store.SaveAsync(WaitCaptureConfiguration.Default));
    }

    [TestMethod]
    public async Task ReadIsBoundedAt64KiBAndOversizedValidJsonCannotEnableHotkeys()
    {
        var enabled = WaitCaptureConfiguration.Default with { FocusedWindow = WaitCaptureConfiguration.Default.FocusedWindow with { Enabled = true } };
        var json = JsonSerializer.Serialize(new { Schema = 1, Capture = enabled }).PadRight(64 * 1024, ' ');
        await File.WriteAllTextAsync(PreferencePath, json);
        var store = new WaitCapturePreferenceStore(PreferencePath);
        var exact = await store.LoadAsync(); Assert.IsNull(exact.Warning); Assert.AreEqual(enabled, exact.Configuration);
        await File.AppendAllTextAsync(PreferencePath, " ");
        var oversized = await store.LoadAsync();
        Assert.IsNotNull(oversized.Warning); Assert.AreEqual(WaitCaptureConfiguration.Default, oversized.Configuration);
        Assert.IsFalse(oversized.Configuration.Bindings().Any(pair => pair.Binding.Enabled));
        Assert.AreEqual(64 * 1024 + 1, new FileInfo(PreferencePath).Length);
    }
}
