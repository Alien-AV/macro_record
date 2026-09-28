using System.ComponentModel;
using MacroRecorder.Waiting;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class MemoryWaitBackendTests
{
    internal static WaitCondition Condition(MemoryScalarType type = MemoryScalarType.Uint64, string expected = "7")
    {
        var wait = WaitValidation.NewMemory(); wait.StableForUs = 0;
        wait.Memory.ExecutablePath = "C:\\test\\target.exe";
        wait.Memory.AbsoluteAddress = 0x1000;
        wait.Memory.ScalarType = type; wait.Memory.Expected = expected;
        return wait;
    }

    private sealed class Process : IMemoryProcess
    {
        public ProcessChoice Identity { get; } = new(12, 100, "C:\\test\\target.exe");
        public int PointerSize { get; set; } = 8;
        public bool IsAlive { get; set; } = true;
        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public readonly List<(ulong Address, int Count)> Requests = [];
        public Func<ulong, int, MemoryReadResult> ReadValue { get; set; } = (_, count) => new(true, BitConverter.GetBytes(7UL)[..count], count);
        public IReadOnlyList<ModuleChoice> ModuleList { get; set; } = [new("C:\\test\\target.exe", "1.2.3", 0x1000, 0x100)];
        public IReadOnlyList<ModuleChoice> Modules(CancellationToken token) => ModuleList;
        public MemoryReadResult Read(ulong address, int count) { Reads++; Requests.Add((address, count)); return ReadValue(address, count); }
        public void Dispose() => Disposed = true;
    }
    private sealed class Api(Process process) : IMemoryProcessApi
    {
        public int Binds;
        public bool Missing;
        public Exception? Error;
        public IMemoryProcess? Bind(string path, CancellationToken token) { Binds++; if (Error is { } error) throw error; return Missing ? null : process; }
    }

    [TestMethod]
    [DataRow(MemoryScalarType.Uint8, "255", 255UL)]
    [DataRow(MemoryScalarType.Int8, "-1", 255UL)]
    [DataRow(MemoryScalarType.Uint16, "65535", 65535UL)]
    [DataRow(MemoryScalarType.Int16, "-32768", 32768UL)]
    [DataRow(MemoryScalarType.Uint32, "4294967295", 4294967295UL)]
    [DataRow(MemoryScalarType.Int32, "-2147483648", 2147483648UL)]
    [DataRow(MemoryScalarType.Uint64, "18446744073709551615", ulong.MaxValue)]
    [DataRow(MemoryScalarType.Int64, "-9223372036854775808", 9223372036854775808UL)]
    public async Task ExactIntegerWidthAndSignedness(MemoryScalarType type, string expected, ulong bits)
    {
        var process = new Process { ReadValue = (_, count) => new(true, BitConverter.GetBytes(bits)[..count], count) };
        await using var observer = new MemoryObserver(new Api(process), () => true);
        var result = await observer.ObserveAsync(Condition(type, expected), default);
        Assert.AreEqual(ObservationState.Match, result.State);
        Assert.AreEqual(ScalarValue.Width(type), process.Requests.Single().Count);
    }

    [TestMethod]
    public async Task UnsignedSixtyFourComparisonDoesNotRoundThroughDouble()
    {
        var process = new Process { ReadValue = (_, n) => new(true, BitConverter.GetBytes(ulong.MaxValue - 1), n) };
        await using var observer = new MemoryObserver(new Api(process), () => true);
        var condition = Condition(expected: ulong.MaxValue.ToString()); condition.Memory.Comparison = NumericComparison.Less;
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
    }

    [TestMethod]
    [DataRow(4)] [DataRow(8)]
    public async Task EverySampleResolvesChainAgainAndChangesUsesLogicalTargetIdentity(int pointerSize)
    {
        ulong allocation = 0x2000; ulong value = 7;
        var process = new Process { PointerSize = pointerSize, ReadValue = (address, count) =>
            new(true, BitConverter.GetBytes(address == 0x1000 ? allocation : value)[..count], count) };
        await using var observer = new MemoryObserver(new Api(process), () => true);
        var condition = Condition(); condition.Trigger = WaitTrigger.Changes; condition.Memory.PointerOffsets.Add(-8);
        var evaluator = new WaitEvaluator(condition);
        var first = await observer.ObserveAsync(condition, default);
        Assert.IsFalse(evaluator.Sample(first, TimeSpan.Zero));
        allocation = 0x4000; value = 8;
        var next = await observer.ObserveAsync(condition, default);
        Assert.IsTrue(evaluator.Sample(next, TimeSpan.FromMilliseconds(100)));
        Assert.AreEqual(first.Identity, next.Identity);
        CollectionAssert.AreEqual(new[] { (0x1000UL, pointerSize), (0x1ff8UL, 8), (0x1000UL, pointerSize), (0x3ff8UL, 8) }, process.Requests.ToArray());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task PartialOrFailedReadsNeverMatchNegativeConditions(bool success)
    {
        var process = new Process { ReadValue = (_, n) => new(success, new byte[n], n - 1) };
        await using var observer = new MemoryObserver(new Api(process), () => true);
        var condition = Condition(); condition.Memory.Comparison = NumericComparison.NumericNotEquals;
        Assert.AreEqual(ObservationState.Unavailable, (await observer.ObserveAsync(condition, default)).State);
    }

    [TestMethod]
    public async Task PermissionFailuresAndProcessRestartsNeverMatchOrRebind()
    {
        var process = new Process(); var api = new Api(process);
        await using var observer = new MemoryObserver(api, () => true);
        var condition = Condition(); condition.Memory.Comparison = NumericComparison.NumericNotEquals;
        Assert.AreEqual(ObservationState.NoMatch, (await observer.ObserveAsync(condition, default)).State);
        process.IsAlive = false;
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(condition, default)).State);
        Assert.AreEqual(1, api.Binds); Assert.AreEqual(1, process.Reads);
        var denied = new Api(new()) { Error = new Win32Exception(5, "denied") };
        await using var second = new MemoryObserver(denied, () => true);
        Assert.AreEqual(ObservationState.Error, (await second.ObserveAsync(condition, default)).State);
    }

    [TestMethod]
    public async Task InitiallyMissingProcessMayBindOnceWhenItAppears()
    {
        var process = new Process(); var api = new Api(process) { Missing = true };
        await using var observer = new MemoryObserver(api, () => true);
        Assert.AreEqual(ObservationState.Unavailable, (await observer.ObserveAsync(Condition(), default)).State);
        api.Missing = false;
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(Condition(), default)).State);
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(Condition(), default)).State);
        Assert.AreEqual(2, api.Binds);
    }

    [TestMethod]
    public async Task RevocationStopsBoundProcessAndMidChainReads()
    {
        var allowed = true; var process = new Process(); var api = new Api(process);
        await using var observer = new MemoryObserver(api, () => allowed);
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(Condition(), default)).State);
        allowed = false;
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(Condition(), default)).State);
        Assert.AreEqual(1, process.Reads);
        allowed = true;
        process.ReadValue = (_, n) => { allowed = false; return new(true, BitConverter.GetBytes(0x2000UL), n); };
        var chain = Condition(); chain.Memory.PointerOffsets.Add(0);
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(chain, default)).State);
        Assert.AreEqual(2, process.Reads);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public async Task InvalidPointerRangesAndOverflowFailBeforeScalarRead(int kind)
    {
        var process = new Process { PointerSize = kind == 0 ? 4 : 8 };
        var condition = Condition();
        if (kind == 0) condition.Memory.AbsoluteAddress = uint.MaxValue;
        else { condition.Memory.PointerOffsets.Add(kind == 1 ? 1 : kind == 2 ? long.MinValue : 0); process.ReadValue = (_, n) => new(true, BitConverter.GetBytes(kind == 1 ? ulong.MaxValue : kind == 2 ? 1UL : 0UL), n); }
        await using var observer = new MemoryObserver(new Api(process), () => true);
        Assert.AreNotEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        Assert.AreEqual(kind == 0 ? 0 : 1, process.Reads);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ModuleInitialReadMustFitImageAndVersionMustMatch(bool chain)
    {
        var process = new Process(); var condition = Condition();
        condition.Memory.Module = new() { Path = "C:\\test\\target.exe", FileVersion = "1.2.3", Offset = 0xff };
        if (chain) condition.Memory.PointerOffsets.Add(0);
        await using var observer = new MemoryObserver(new Api(process), () => true);
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(condition, default)).State);
        Assert.AreEqual(0, process.Reads);
        condition.Memory.Module.Offset = 0; condition.Memory.Module.FileVersion = "different";
        Assert.AreEqual(ObservationState.Error, (await observer.ObserveAsync(condition, default)).State);
        Assert.AreEqual(0, process.Reads);
    }

    [TestMethod]
    [DataRow(double.NaN)] [DataRow(double.PositiveInfinity)] [DataRow(double.NegativeInfinity)]
    public async Task NonfiniteSamplesCannotMatchInequality(double value)
    {
        var process = new Process { ReadValue = (_, n) => new(true, BitConverter.GetBytes(value), n) };
        await using var observer = new MemoryObserver(new Api(process), () => true);
        var condition = Condition(MemoryScalarType.Float64); condition.Memory.Comparison = NumericComparison.NumericNotEquals;
        Assert.AreEqual(ObservationState.Unavailable, (await observer.ObserveAsync(condition, default)).State);
    }

    [TestMethod]
    public async Task FloatToleranceAndOrderingUseFiniteTypedSamples()
    {
        var process = new Process { ReadValue = (_, n) => new(true, BitConverter.GetBytes(7.125), n) };
        await using var observer = new MemoryObserver(new Api(process), () => true);
        var condition = Condition(MemoryScalarType.Float64); condition.Memory.Tolerance = .125;
        Assert.AreEqual(ObservationState.Match, (await observer.ObserveAsync(condition, default)).State);
        condition.Memory.Comparison = NumericComparison.NumericNotEquals;
        Assert.AreEqual(ObservationState.NoMatch, (await observer.ObserveAsync(condition, default)).State);
    }

    [TestMethod]
    [DataRow("+-1", false)] [DataRow("++1", false)] [DataRow("-+1", false)] [DataRow("--1", false)]
    [DataRow("+1", true)] [DataRow("-0", true)] [DataRow("0", true)]
    public void ScalarLeadingSignRulesMatchNative(string expected, bool valid)
    {
        foreach (var type in new[] { MemoryScalarType.Int64, MemoryScalarType.Uint64, MemoryScalarType.Float64 })
        {
            if (valid) ScalarValue.Parse(type, expected);
            else Assert.Throws<ArgumentException>(() => ScalarValue.Parse(type, expected));
        }
    }

    [TestMethod]
    [DataRow("1e+2", true)] [DataRow("1e-2", true)] [DataRow("+1.0e-2", true)]
    [DataRow("1e+-2", false)] [DataRow("1e++2", false)] [DataRow("1e--2", false)]
    public void FloatingExponentSignRulesMatchNative(string expected, bool valid)
    {
        if (valid) ScalarValue.Parse(MemoryScalarType.Float64, expected);
        else Assert.Throws<ArgumentException>(() => ScalarValue.Parse(MemoryScalarType.Float64, expected));
    }
}
