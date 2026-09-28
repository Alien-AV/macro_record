using System.Diagnostics;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class OcrPackagingTests
{
    [TestMethod]
    public async Task PreparedPortableOcrRejectsMissingAndStaleAssetsAndRecognizesOwnedFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "Test-OcrPackaging.ps1")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "Packaging regression script must be available in the source checkout.");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = directory.FullName
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/Test-OcrPackaging.ps1" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var text = await output;
        Assert.AreEqual(0, process.ExitCode, text + await error);
        StringAssert.Contains(text, "generated fixture OCR");
        var fixturePath = text.Split('\n').Single(line => line.StartsWith("BMP-FIXTURE:", StringComparison.Ordinal))[12..].Trim();
        var bundle = Path.Combine(directory.FullName, "artifacts", "ocr");
        var cli = new ProcessStartInfo(Path.Combine(bundle, "tesseract.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        foreach (var argument in new[] { "stdin", "stdout", "--tessdata-dir", Path.Combine(bundle, "tessdata"), "-l", "eng", "--psm", "6" })
            cli.ArgumentList.Add(argument);
        cli.Environment["PATH"] = Environment.SystemDirectory;
        cli.Environment.Remove("TESSDATA_PREFIX");
        using var ocr = Process.Start(cli)!;
        var recognized = ocr.StandardOutput.ReadToEndAsync();
        var diagnostics = ocr.StandardError.ReadToEndAsync();
        try
        {
            var bitmap = await File.ReadAllBytesAsync(fixturePath, timeout.Token);
            await ocr.StandardInput.BaseStream.WriteAsync(bitmap, timeout.Token);
            ocr.StandardInput.Close();
            await ocr.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, ocr.ExitCode, await diagnostics);
            Assert.AreEqual("PORTABLE OCR READY 12345", (await recognized).Trim(), "Backend-compatible top-down BGRA BMP on stdin.");
        }
        finally { if (!ocr.HasExited) { ocr.Kill(entireProcessTree: true); await ocr.WaitForExitAsync(); } }
    }
}
