using MacroRecorder.Waiting;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitSourcePreferencesTests
{
    private DirectoryInfo _directory = null!;
    private string PreferencePath => Path.Combine(_directory.FullName, "wait-sources.json");
    [TestInitialize] public void Initialize() => _directory = Directory.CreateTempSubdirectory("macro-wait-preferences-");
    [TestCleanup] public void Cleanup() => _directory.Delete(recursive: true);

    internal sealed class MemoryStore : IWaitSourcePreferenceStore
    {
        internal WaitLocalOptions Value = new();
        internal int Loads, Saves;
        internal bool FailSave;
        public Task<WaitSourcePreferenceData> LoadAsync(CancellationToken token)
        { Loads++; token.ThrowIfCancellationRequested(); return Task.FromResult(new WaitSourcePreferenceData(Value)); }
        public Task SaveAsync(WaitLocalOptions options, CancellationToken token)
        {
            Saves++; token.ThrowIfCancellationRequested();
            if (FailSave) throw new IOException("Injected save failure");
            Value = options; return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task ConstructionDoesNotLoadOrGrantAndOnlyExplicitSaveChangesPolicy()
    {
        var store = new MemoryStore { Value = new(true) }; var local = new WaitLocalSettings();
        var preferences = new WaitSourcePreferences(store, local);
        Assert.AreEqual(0, store.Loads); Assert.IsFalse(preferences.Options.MemoryEnabled); Assert.IsFalse(local.MemoryEnabled);
        await preferences.InitializeAsync();
        Assert.AreEqual(1, store.Loads); Assert.IsTrue(local.MemoryEnabled);
        await preferences.SetMemoryEnabledAsync(false);
        Assert.AreEqual(1, store.Saves); Assert.IsFalse(local.MemoryEnabled);
        await preferences.InitializeAsync(); Assert.AreEqual(1, store.Loads);
    }

    [TestMethod]
    public async Task LocalPermissionAndOcrInstallationSurviveRestartWithoutMacroData()
    {
        var first = new WaitSourcePreferences(new WaitSourcePreferenceStore(PreferencePath), new());
        await first.SetOcrInstallationAsync(@"C:\Tools\Tesseract\tesseract.exe", @"C:\Tools\Tesseract\tessdata");
        await first.SetMemoryEnabledAsync(true);
        var local = new WaitLocalSettings(); var next = new WaitSourcePreferences(new WaitSourcePreferenceStore(PreferencePath), local);
        Assert.IsFalse(local.MemoryEnabled);
        await next.InitializeAsync();
        Assert.IsTrue(local.MemoryEnabled); Assert.AreEqual(first.Options, next.Options);
        var json = await File.ReadAllTextAsync(PreferencePath);
        Assert.IsFalse(json.Contains("PointerOffsets")); Assert.IsFalse(json.Contains("AbsoluteAddress")); Assert.IsFalse(json.Contains("Expected"));
        Assert.AreEqual(1, _directory.GetFiles().Length, "Atomic persistence leaves no temporary files.");
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{\"Schema\":2,\"MemoryEnabled\":true,\"TesseractExecutablePath\":\"\",\"TessdataDirectory\":\"\"}")]
    [DataRow("{\"Schema\":1,\"MemoryEnabled\":true,\"MemoryEnabled\":true,\"TesseractExecutablePath\":\"\",\"TessdataDirectory\":\"\"}")]
    [DataRow("{\"Schema\":1,\"MemoryEnabled\":\"true\",\"TesseractExecutablePath\":\"\",\"TessdataDirectory\":\"\"}")]
    [DataRow("{\"Schema\":1,\"MemoryEnabled\":true}")]
    [DataRow("{\"Schema\":1,\"MemoryEnabled\":true,\"TesseractExecutablePath\":\"relative.exe\",\"TessdataDirectory\":\"\"}")]
    public async Task MalformedSettingsFailClosedAndWarn(string contents)
    {
        await File.WriteAllTextAsync(PreferencePath, contents);
        var local = new WaitLocalSettings(); var preferences = new WaitSourcePreferences(new WaitSourcePreferenceStore(PreferencePath), local);
        await preferences.InitializeAsync();
        Assert.IsFalse(local.MemoryEnabled); Assert.AreEqual(new WaitLocalOptions(), preferences.Options); Assert.IsNotNull(preferences.Warning);
        Assert.AreEqual(contents, await File.ReadAllTextAsync(PreferencePath), "Loading does not repair or overwrite settings.");
    }

    [TestMethod]
    public async Task FailedOrCancelledSaveCannotGrantMemoryAccess()
    {
        var store = new MemoryStore { FailSave = true }; var local = new WaitLocalSettings();
        var preferences = new WaitSourcePreferences(store, local);
        await Assert.ThrowsExactlyAsync<IOException>(() => preferences.SetMemoryEnabledAsync(true));
        Assert.IsFalse(local.MemoryEnabled); Assert.IsFalse(preferences.Options.MemoryEnabled);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => preferences.SetMemoryEnabledAsync(true, cancellation.Token));
        Assert.IsFalse(local.MemoryEnabled);
    }

    [TestMethod]
    public async Task MissingSettingsKeepMemoryDisabledAndDoNotCreateAFile()
    {
        var local = new WaitLocalSettings(); var preferences = new WaitSourcePreferences(new WaitSourcePreferenceStore(PreferencePath), local);
        await preferences.InitializeAsync();
        Assert.IsFalse(local.MemoryEnabled); Assert.IsNull(preferences.Warning); Assert.IsFalse(File.Exists(PreferencePath));
    }

    [TestMethod]
    public async Task ConcurrentIndependentSettingsEditsPreserveBothValues()
    {
        var preferences = new WaitSourcePreferences(new MemoryStore(), new());
        await Task.WhenAll(preferences.SetMemoryEnabledAsync(true), preferences.SetOcrInstallationAsync(@"C:\ocr\tesseract.exe", @"C:\ocr\tessdata"));
        Assert.IsTrue(preferences.Options.MemoryEnabled); Assert.AreEqual(@"C:\ocr\tesseract.exe", preferences.Options.TesseractExecutablePath);
    }
}
