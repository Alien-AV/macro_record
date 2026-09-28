using System.Reflection;
using System.Runtime.InteropServices;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class RecordingExportContractTests
{
    [TestMethod]
    public void CaptureSnapshotStartRequiresVersionedFourArgumentExportWithoutInitializingCapture()
    {
        var native = typeof(NativeRecordingTransport).GetNestedType("NativeApi", BindingFlags.NonPublic)!;
        var start = native.GetMethod("DllStartRecord", BindingFlags.Static | BindingFlags.NonPublic)!;
        var import = start.GetCustomAttribute<DllImportAttribute>()!;
        Assert.AreEqual("RecordPlaybackDLL.dll", import.Value);
        Assert.AreEqual("iac_dll_start_record_v2", import.EntryPoint);
        Assert.AreEqual(CallingConvention.Cdecl, import.CallingConvention);
        CollectionAssert.AreEqual(new[] { typeof(ulong), typeof(RecordingStopGestures), typeof(uint[]), typeof(uint) },
            start.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.AreEqual(typeof(bool), start.ReturnType);
        Assert.AreEqual(UnmanagedType.I1, start.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()!.Value);
        Assert.IsFalse(native.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Any(method => method.GetCustomAttribute<DllImportAttribute>()?.EntryPoint == "iac_dll_start_record"));
    }
}
