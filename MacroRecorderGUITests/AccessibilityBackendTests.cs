using System.Runtime.InteropServices;
using MacroRecorder.Waiting;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
[DoNotParallelize]
public sealed class AccessibilityBackendTests
{
    private static readonly WaitWindow Window = new(123, 45, 678);
    private static AccessibilityTextCondition Condition() => new()
    {
        Element = new() { AutomationId = "result", ControlType = 50020 },
        Source = AccessibilityTextSource.TextPattern,
        Predicate = new() { Comparison = TextComparison.TextNotContains, Expected = "secret" }
    };

    private sealed class Node(nint handle, WindowsAccessibilityInfo info)
    {
        public nint Handle = handle;
        public WindowsAccessibilityInfo Info = info;
        public List<Node> Children = [];
        public Node? Parent;
    }

    private sealed class Api : IWindowsAccessibilityApi
    {
        public readonly Dictionary<nint, Node> Nodes = [];
        public readonly List<(string Name, int Thread, ApartmentState Apartment)> Calls = [];
        public readonly Dictionary<AccessibilityTextSource, WindowsAccessibilityText> Text = new()
        {
            [AccessibilityTextSource.AccessibleName] = new("name"),
            [AccessibilityTextSource.TextPattern] = new("document text"),
            [AccessibilityTextSource.ValuePattern] = new("value")
        };
        public Node Root, Parent, Target;
        public WaitWindow CurrentWindow = Window;
        public bool RootMissing, Disposed;
        public int Opens, TextReads, InfoReads, WindowChecks, Created;
        public int RequestedLength;
        public Action<string>? BeforeCall;
        public Action? OnRead;
        public Api()
        {
            Root = Add(null, "root", 50032);
            Root.Info = Root.Info with { ProcessId = Window.ProcessId, NativeWindow = Window.Handle };
            Parent = Add(Root, "panel", 50033);
            Target = Add(Parent, "result", 50020);
        }
        public Node Add(Node? parent, string id, uint type = 50020)
        {
            var handle = (nint)(Nodes.Count + 1);
            var node = new Node(handle, new(id, type, $"runtime-{handle}")) { Parent = parent };
            Nodes.Add(handle, node);
            parent?.Children.Add(node);
            return node;
        }
        public IWindowsAccessibilityApi Create() { Created++; Call("create"); return this; }
        private void Call(string name)
        {
            Assert.IsFalse(Disposed, "A released API must not be reused.");
            Calls.Add((name, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            BeforeCall?.Invoke(name);
        }
        public bool IsWindowCurrent(WaitWindow window) { WindowChecks++; Call("window"); return window == CurrentWindow; }
        public nint OpenWindow(nint window)
        {
            Opens++; Call("open"); Assert.AreEqual(Window.Handle, window);
            return RootMissing ? 0 : Root.Handle;
        }
        public nint FirstChild(nint element) { Call("child"); return Nodes[element].Children.FirstOrDefault()?.Handle ?? 0; }
        public nint NextSibling(nint element)
        {
            Call("sibling");
            var node = Nodes[element];
            var siblings = node.Parent?.Children;
            return siblings?.Skip(siblings.IndexOf(node) + 1).FirstOrDefault()?.Handle ?? 0;
        }
        public WindowsAccessibilityInfo GetInfo(nint element) { InfoReads++; Call("info"); return Nodes[element].Info; }
        public WindowsAccessibilityText ReadText(nint element, AccessibilityTextSource source, int maximumLength)
        {
            Call("text"); TextReads++; RequestedLength = maximumLength;
            Assert.AreEqual(Target.Handle, element);
            OnRead?.Invoke();
            if (!Text.TryGetValue(source, out var value)) throw new COMException("Unsupported", unchecked((int)0x80040204));
            return value;
        }
        public void Dispose() { Call("dispose"); Disposed = true; }
    }

    private static void Failed(TextReadResult result, ReadStatus status = ReadStatus.Unavailable)
    {
        Assert.AreEqual(status, result.Status, result.Detail);
        Assert.AreEqual("", result.Text);
        Assert.AreEqual("", result.Identity);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Detail));
    }

    [TestMethod]
    public async Task ConstructionAndUnusedDisposalHaveNoNativeEffects()
    {
        var api = new Api();
        await using (var backend = new WindowsAccessibilityTextBackend(api.Create)) { }
        await using (var backend = new WindowsAccessibilityTextBackend()) { }
        Assert.AreEqual(0, api.Created);
        Assert.AreEqual(0, api.Calls.Count);
    }

    [TestMethod]
    [DataRow(AccessibilityTextSource.AccessibleName, "name")]
    [DataRow(AccessibilityTextSource.TextPattern, "document text")]
    [DataRow(AccessibilityTextSource.ValuePattern, "value")]
    public async Task SourcesAreDistinctCompleteAndScoped(AccessibilityTextSource source, string expected)
    {
        var api = new Api();
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var condition = Condition(); condition.Source = source;
        condition.Ancestors.Add(new AccessibilitySelector { AutomationId = "panel", ControlType = 50033 });
        condition.Ancestors.Add(new AccessibilitySelector { AutomationId = "root", ControlType = 50032 });
        var result = await backend.ReadAsync(Window, condition, default);
        Assert.AreEqual(ReadStatus.Success, result.Status, result.Detail);
        Assert.AreEqual(expected, result.Text);
        Assert.AreEqual($"{Window.Identity}/uia/{api.Target.Info.RuntimeIdentity}", result.Identity);
        Assert.AreEqual(32769, api.RequestedLength);
        Assert.AreEqual(1, api.TextReads);
        Assert.AreEqual(2, api.Opens);
        Assert.IsTrue(api.WindowChecks >= 4);
        Assert.IsTrue(api.Disposed);
        Assert.AreEqual(1, api.Calls.Select(call => call.Thread).Distinct().Count());
        Assert.IsTrue(api.Calls.All(call => call.Apartment == ApartmentState.MTA));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("a\0b\r\n  c")]
    public async Task EmptyAndUnnormalizedTextAreComplete(string text)
    {
        var api = new Api(); api.Text[AccessibilityTextSource.TextPattern] = new(text);
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var result = await backend.ReadAsync(Window, Condition(), default);
        Assert.AreEqual(ReadStatus.Success, result.Status);
        Assert.AreEqual(text, result.Text);
    }

    [TestMethod]
    [DataRow(32768, true, ReadStatus.Success)]
    [DataRow(32769, true, ReadStatus.Unavailable)]
    [DataRow(10, false, ReadStatus.Unavailable)]
    [DataRow(0, false, ReadStatus.Unavailable)]
    public async Task OverflowAndPartialTextNeverBecomeNegativeEvidence(int length, bool complete, ReadStatus expected)
    {
        var api = new Api(); api.Text[AccessibilityTextSource.TextPattern] = new(new string('x', length), complete);
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var result = await backend.ReadAsync(Window, Condition(), default);
        Assert.AreEqual(expected, result.Status);
        if (expected == ReadStatus.Success) Assert.AreEqual(length, result.Text.Length);
        else Failed(result);
        Assert.IsTrue(api.Disposed);
    }

    [TestMethod]
    [DataRow(AccessibilityTextSource.AccessibleName)]
    [DataRow(AccessibilityTextSource.TextPattern)]
    [DataRow(AccessibilityTextSource.ValuePattern)]
    public async Task UnsupportedSourceNeverFallsBack(AccessibilityTextSource source)
    {
        var api = new Api(); api.Text.Remove(source);
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var condition = Condition(); condition.Source = source;
        Failed(await backend.ReadAsync(Window, condition, default));
        Assert.AreEqual(1, api.TextReads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PasswordElementsAndDescendantsAreNotRead(bool ancestor)
    {
        var api = new Api(); var node = ancestor ? api.Parent : api.Target;
        node.Info = node.Info with { IsPassword = true };
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, Condition(), default));
        Assert.AreEqual(0, api.TextReads);
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("type")]
    [DataRow("case")]
    [DataRow("skipped-parent")]
    [DataRow("reversed")]
    public async Task SelectorsAndNearestFirstAncestorsMustMatchExactly(string mismatch)
    {
        var api = new Api(); var condition = Condition();
        if (mismatch == "id") condition.Element.AutomationId = "other";
        if (mismatch == "type") condition.Element.ControlType = 50004;
        if (mismatch == "case") condition.Element.AutomationId = "RESULT";
        if (mismatch == "skipped-parent") condition.Ancestors.Add(new AccessibilitySelector { AutomationId = "root" });
        if (mismatch == "reversed")
        {
            condition.Ancestors.Add(new AccessibilitySelector { AutomationId = "root" });
            condition.Ancestors.Add(new AccessibilitySelector { AutomationId = "panel" });
        }
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, condition, default));
        Assert.AreEqual(0, api.TextReads);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task EitherIdentificationFieldCanBeUsed(bool idOnly)
    {
        var api = new Api(); var condition = Condition();
        if (idOnly) condition.Element.ControlType = 0; else condition.Element.AutomationId = "";
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Assert.AreEqual(ReadStatus.Success, (await backend.ReadAsync(Window, condition, default)).Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DuplicateMatchesBeforeOrAfterReadAreErrors(bool afterRead)
    {
        var api = new Api();
        if (afterRead) api.OnRead = () => api.Add(api.Parent, "result");
        else api.Add(api.Parent, "result");
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, Condition(), default), ReadStatus.Error);
        Assert.AreEqual(afterRead ? 1 : 0, api.TextReads);
    }

    [TestMethod]
    [DataRow("handle")]
    [DataRow("pid")]
    [DataRow("created")]
    [DataRow("root-handle")]
    [DataRow("root-pid")]
    [DataRow("missing-root")]
    public async Task InvalidWindowIdentityFailsBeforeReadingText(string mismatch)
    {
        var api = new Api();
        if (mismatch == "handle") api.CurrentWindow = Window with { Handle = 999 };
        if (mismatch == "pid") api.CurrentWindow = Window with { ProcessId = 999 };
        if (mismatch == "created") api.CurrentWindow = Window with { ProcessCreated = 999 };
        if (mismatch == "root-handle") api.Root.Info = api.Root.Info with { NativeWindow = 999 };
        if (mismatch == "root-pid") api.Root.Info = api.Root.Info with { ProcessId = 999 };
        if (mismatch == "missing-root") api.RootMissing = true;
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, Condition(), default));
        Assert.AreEqual(0, api.TextReads);
        Assert.IsFalse(api.Calls.Any(call => call.Name == "child"), "Do not walk a root belonging to another window.");
    }

    [TestMethod]
    [DataRow("element")]
    [DataRow("root")]
    [DataRow("ancestor")]
    [DataRow("password")]
    [DataRow("removed")]
    [DataRow("replaced")]
    [DataRow("reparented")]
    [DataRow("process")]
    [DataRow("window")]
    public async Task StaleOrRecreatedIdentityDuringReadCannotSupplyNegativeEvidence(string change)
    {
        var api = new Api();
        api.OnRead = () =>
        {
            if (change == "element") api.Target.Info = api.Target.Info with { RuntimeIdentity = "replacement" };
            if (change == "root") api.Root.Info = api.Root.Info with { RuntimeIdentity = "replacement" };
            if (change == "ancestor") api.Parent.Info = api.Parent.Info with { RuntimeIdentity = "replacement" };
            if (change == "password") api.Target.Info = api.Target.Info with { IsPassword = true };
            if (change is "removed" or "replaced" or "reparented") api.Parent.Children.Remove(api.Target);
            if (change == "replaced") api.Add(api.Parent, "result");
            if (change == "reparented") { api.Root.Children.Add(api.Target); api.Target.Parent = api.Root; }
            if (change == "process") api.CurrentWindow = Window with { ProcessCreated = 999 };
            if (change == "window") api.CurrentWindow = Window with { Handle = 999 };
        };
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, Condition(), default));
        Assert.IsTrue(api.Disposed);
    }

    [TestMethod]
    public async Task ElementIdentityIsStableAcrossTextChangesButChangesForReplacement()
    {
        var identities = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var api = new Api(); api.Text[AccessibilityTextSource.TextPattern] = new($"value-{index}");
            if (index == 2) api.Target.Info = api.Target.Info with { RuntimeIdentity = "replacement" };
            await using var backend = new WindowsAccessibilityTextBackend(api.Create);
            var result = await backend.ReadAsync(Window, Condition(), default);
            Assert.AreEqual(ReadStatus.Success, result.Status);
            identities.Add(result.Identity);
        }
        Assert.AreEqual(identities[0], identities[1]);
        Assert.AreNotEqual(identities[1], identities[2]);
    }

    [TestMethod]
    [DataRow(512, ReadStatus.Success)]
    [DataRow(513, ReadStatus.Unavailable)]
    public async Task CandidateEnumerationMustBeCompleteWithinTheCap(int total, ReadStatus expected)
    {
        var api = new Api();
        while (api.Nodes.Count < total) api.Add(api.Root, $"other-{api.Nodes.Count}");
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var result = await backend.ReadAsync(Window, Condition(), default);
        Assert.AreEqual(expected, result.Status);
        if (expected != ReadStatus.Success) { Failed(result); Assert.AreEqual(0, api.TextReads); Assert.AreEqual(512, api.InfoReads); }
    }

    [TestMethod]
    [DataRow(16, ReadStatus.Success)]
    [DataRow(17, ReadStatus.Unavailable)]
    public async Task DepthLimitIsCompleteOrUnavailable(int depth, ReadStatus expected)
    {
        var api = new Api(); var parent = api.Root;
        for (var index = 0; index < depth; index++) parent = api.Add(parent, $"deep-{index}");
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var result = await backend.ReadAsync(Window, Condition(), default);
        Assert.AreEqual(expected, result.Status);
        if (expected != ReadStatus.Success) { Failed(result); Assert.AreEqual(0, api.TextReads); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("cycle")]
    public async Task MissingOrRepeatedRuntimeIdentityNeverProducesText(string failure)
    {
        var api = new Api();
        if (failure == "missing") api.Target.Info = api.Target.Info with { RuntimeIdentity = "" };
        if (failure == "duplicate") api.Target.Info = api.Target.Info with { RuntimeIdentity = api.Root.Info.RuntimeIdentity };
        if (failure == "cycle") api.Target.Children.Add(api.Root);
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, Condition(), default));
        Assert.AreEqual(0, api.TextReads);
    }

    [TestMethod]
    [DataRow("window")]
    [DataRow("open")]
    [DataRow("info")]
    [DataRow("child")]
    [DataRow("sibling")]
    [DataRow("text")]
    [DataRow("dispose")]
    public async Task NativeFailuresAndCleanupFailuresDiscardPartialResults(string stage)
    {
        var api = new Api();
        api.BeforeCall = name => { if (name == stage) throw new COMException("Unavailable", unchecked((int)0x80040201)); };
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, Condition(), default));
        Assert.AreEqual(1, api.Calls.Count(call => call.Name == "dispose"));
    }

    [TestMethod]
    public async Task InitializationFailureIsUnavailableAndDoesNotPoisonTheWorker()
    {
        await using (var backend = new WindowsAccessibilityTextBackend(() => throw new COMException("No UIA")))
            Failed(await backend.ReadAsync(Window, Condition(), default));
        var api = new Api();
        await using var next = new WindowsAccessibilityTextBackend(api.Create);
        Assert.AreEqual(ReadStatus.Success, (await next.ReadAsync(Window, Condition(), default)).Status);
    }

    [TestMethod]
    [DataRow("element")]
    [DataRow("source")]
    [DataRow("ancestor")]
    [DataRow("depth")]
    public async Task InvalidDefinitionDoesNotCreateNativeApi(string failure)
    {
        var api = new Api(); var condition = Condition();
        if (failure == "element") condition.Element = new();
        if (failure == "source") condition.Source = (AccessibilityTextSource)999;
        if (failure == "ancestor") condition.Ancestors.Add(new AccessibilitySelector());
        if (failure == "depth") for (var index = 0; index < 17; index++) condition.Ancestors.Add(new AccessibilitySelector { AutomationId = "parent" });
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        Failed(await backend.ReadAsync(Window, condition, default), ReadStatus.Error);
        Assert.AreEqual(0, api.Created);
    }

    [TestMethod]
    public async Task ChoicesAreBoundedIdentifyingSelectorsAndNeverReadText()
    {
        var api = new Api();
        var password = api.Add(api.Root, "password"); password.Info = password.Info with { IsPassword = true };
        api.Add(password, "password-child");
        api.Add(api.Parent, "duplicate"); api.Add(api.Parent, "duplicate");
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var result = await backend.GetChoicesAsync(Window, default);
        Assert.AreEqual(ReadStatus.Success, result.Status, result.Detail);
        Assert.AreEqual(3, result.Choices.Count);
        var choice = result.Choices.Single(choice => choice.Element.AutomationId == "result");
        Assert.AreEqual(50020u, choice.Element.ControlType);
        CollectionAssert.AreEqual(new[] { "panel", "root" }, choice.Ancestors.Select(ancestor => ancestor.AutomationId).ToArray());
        StringAssert.Contains(choice.Label, "result"); StringAssert.Contains(choice.Label, "panel");
        Assert.AreEqual(0, api.TextReads);
        Assert.IsTrue(api.Disposed);
    }

    [TestMethod]
    [DataRow("overflow")]
    [DataRow("changed")]
    [DataRow("failure")]
    public async Task IncompleteChoicesDoNotLeakPartialSelectors(string failure)
    {
        var api = new Api();
        if (failure == "overflow") while (api.Nodes.Count < 513) api.Add(api.Root, $"other-{api.Nodes.Count}");
        api.BeforeCall = name =>
        {
            if (failure == "changed" && name == "open" && api.Opens == 2) api.Target.Info = api.Target.Info with { RuntimeIdentity = "new" };
            if (failure == "failure" && name == "sibling") throw new COMException("Unavailable");
        };
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var result = await backend.GetChoicesAsync(Window, default);
        Assert.AreEqual(ReadStatus.Unavailable, result.Status);
        Assert.AreEqual(0, result.Choices.Count);
        Assert.AreEqual(0, api.TextReads);
    }

    [TestMethod]
    public async Task PrecancelledRequestsNeverCreateNativeApi()
    {
        var api = new Api(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await backend.ReadAsync(Window, Condition(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await backend.GetChoicesAsync(Window, cancellation.Token));
        Assert.AreEqual(0, api.Created);
    }

    [TestMethod]
    [DataRow("info")]
    [DataRow("text")]
    [DataRow("dispose")]
    public async Task CancellationDuringNativeWorkWaitsForCleanup(string stage)
    {
        var api = new Api(); using var cancellation = new CancellationTokenSource();
        api.BeforeCall = name => { if (name == stage) cancellation.Cancel(); };
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () => await backend.ReadAsync(Window, Condition(), cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(api.Disposed);
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("dispose")]
    public async Task HungCallRetainsOwnershipAndRejectsNewWorkUntilCleanupEnds(string stage)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var api = new Api();
        api.BeforeCall = name => { if (name == stage) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test gate"); } };
        var backend = new WindowsAccessibilityTextBackend(api.Create);
        var pending = backend.ReadAsync(Window, Condition(), cancellation.Token).AsTask();
        Task disposal = Task.CompletedTask;
        var otherApi = new Api();
        await using var other = new WindowsAccessibilityTextBackend(otherApi.Create);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            Failed(await backend.ReadAsync(Window, Condition(), default));
            disposal = backend.DisposeAsync().AsTask();
            Assert.IsFalse(pending.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(api.Disposed);
            Failed(await other.ReadAsync(Window, Condition(), default));
            Assert.AreEqual(0, otherApi.Created);
        }
        finally { release.Set(); }
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        await disposal;
        Assert.IsTrue(api.Disposed);
        Assert.AreEqual(ReadStatus.Success, (await other.ReadAsync(Window, Condition(), default)).Status);
        Assert.AreEqual(api.Calls[0].Thread, otherApi.Calls[0].Thread, "The bounded worker is reused.");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await backend.ReadAsync(Window, Condition(), default));
        await backend.DisposeAsync();
    }

    [TestMethod]
    public async Task DefinitionIsSnapshottedBeforeStartingTheMtaRequest()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var api = new Api(); var condition = Condition();
        api.BeforeCall = name => { if (name == "create") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test gate"); } };
        await using var backend = new WindowsAccessibilityTextBackend(api.Create);
        var pending = backend.ReadAsync(Window, condition, default).AsTask();
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            condition.Element.AutomationId = "changed"; condition.Source = AccessibilityTextSource.ValuePattern;
        }
        finally { release.Set(); }
        var result = await pending;
        Assert.AreEqual(ReadStatus.Success, result.Status);
        Assert.AreEqual("document text", result.Text);
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(32768, true)]
    [DataRow(32769, false)]
    public void NativeBstrLengthIsCheckedBeforeManagedCopyAndOwnedStringIsFreed(int length, bool complete)
    {
        var freed = 0;
        var result = WindowsAccessibilityNativeApi.ReadBstr((out nint pointer) =>
        {
            pointer = Marshal.StringToBSTR(new string('x', length)); return 0;
        }, 32768, pointer => { freed++; Marshal.FreeBSTR(pointer); });
        Assert.AreEqual(complete, result.Complete);
        Assert.AreEqual(Math.Min(length, 32768), result.Text.Length);
        Assert.AreEqual(1, freed);
    }

    [TestMethod]
    public void NativeBstrCopyPreservesEmbeddedNullAndUtf16Units()
    {
        const string original = "a\0\ud83d\ude42b";
        var result = WindowsAccessibilityNativeApi.ReadBstr((out nint pointer) =>
        {
            pointer = Marshal.StringToBSTR(original); return 0;
        }, original.Length);
        Assert.AreEqual(original, result.Text);
        Assert.IsTrue(result.Complete);
        var empty = WindowsAccessibilityNativeApi.ReadBstr((out nint pointer) => { pointer = 0; return 0; }, 1);
        Assert.AreEqual("", empty.Text); Assert.IsTrue(empty.Complete);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeBstrIsFreedOnFailedHresultOrThrowingGetter(bool throws)
    {
        var freed = 0;
        Assert.ThrowsExactly<COMException>(() => WindowsAccessibilityNativeApi.ReadBstr((out nint pointer) =>
        {
            pointer = Marshal.StringToBSTR("partial");
            if (throws) throw new COMException("Failed after allocating");
            return unchecked((int)0x80004005);
        }, 32768, pointer => { freed++; Marshal.FreeBSTR(pointer); }));
        Assert.AreEqual(1, freed);
    }

    [TestMethod]
    public void NativeRuntimeIdTreatsIntegersAsOpaqueAndHonorsNonzeroSafeArrayLowerBound()
    {
        var freed = 0;
        var value = WindowsAccessibilityNativeApi.ReadRuntimeId((out nint pointer) =>
        {
            pointer = SafeArrayCreateVector(3, -4, 3);
            Assert.AreNotEqual((nint)0, pointer);
            Assert.AreEqual(0, SafeArrayAccessData(pointer, out var data));
            try { Marshal.Copy(new[] { 42, -1, int.MinValue }, 0, data, 3); }
            finally { Assert.AreEqual(0, SafeArrayUnaccessData(pointer)); }
            return 0;
        }, pointer => { freed++; Assert.AreEqual(0, SafeArrayDestroy(pointer), "SAFEARRAY must be unlocked before destruction."); });
        Assert.AreEqual("0000002A.FFFFFFFF.80000000", value);
        Assert.AreEqual(1, freed);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("empty")]
    [DataRow("overflow")]
    [DataRow("type")]
    [DataRow("rank")]
    [DataRow("hresult")]
    [DataRow("throw")]
    public void NativeRuntimeIdFailuresReleaseOwnedSafeArray(string failure)
    {
        var freed = 0;
        Assert.Throws<Exception>(() => WindowsAccessibilityNativeApi.ReadRuntimeId((out nint pointer) =>
        {
            if (failure == "null") pointer = 0;
            else if (failure == "rank") pointer = SafeArrayCreate(3, 2, [new(2, 0), new(2, 0)]);
            else pointer = SafeArrayCreateVector(failure == "type" ? (ushort)19 : (ushort)3, 0,
                failure == "empty" ? 0u : failure == "overflow" ? 129u : 2u);
            if (failure == "throw") throw new COMException("Failed after allocating");
            return failure == "hresult" ? unchecked((int)0x80004005) : 0;
        }, pointer => { freed++; Assert.AreEqual(0, SafeArrayDestroy(pointer)); }));
        Assert.AreEqual(failure == "null" ? 0 : 1, freed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ArrayBound(uint count, int lower)
    {
        public readonly uint Count = count;
        public readonly int Lower = lower;
    }
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern nint SafeArrayCreateVector(ushort type, int lower, uint count);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern nint SafeArrayCreate(ushort type, uint dimensions, [In] ArrayBound[] bounds);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayDestroy(nint array);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayAccessData(nint array, out nint data);
    [DllImport("oleaut32.dll", ExactSpelling = true)] private static extern int SafeArrayUnaccessData(nint array);
}
