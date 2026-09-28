using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace MacroRecorder.Waiting;

internal sealed record OcrCommandResult(int ExitCode, string Text, string Error);
internal interface IOcrCommand
{
    Task<OcrCommandResult> RunAsync(string executable, string tessdata, string language, byte[] bitmap, CancellationToken token);
}

internal sealed class TesseractOcrBackend(WaitLocalOptions options, IOcrCommand? command = null) : IOcrBackend
{
    private readonly IOcrCommand _command = command ?? new LocalOcrCommand();
    internal static ChoiceResult<string> GetLanguages(WaitLocalOptions options, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(options.TesseractExecutablePath) || !File.Exists(options.TesseractExecutablePath)
                || !Path.IsPathFullyQualified(options.TessdataDirectory) || !Directory.Exists(options.TessdataDirectory))
                return new(ReadStatus.Unavailable, [], "Install or configure local Tesseract and its language-data directory.", false);
            var languages = new List<string>();
            foreach (var path in Directory.EnumerateFiles(options.TessdataDirectory, "*.traineddata", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if (languages.Count >= 256) return new(ReadStatus.Error, [], "OCR language listing exceeds 256 entries.", false);
                var id = Path.GetFileNameWithoutExtension(path);
                if (id.Length is < 1 or > 64 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    return new(ReadStatus.Unavailable, [], "OCR language directory contains an unsupported language identifier.", false);
                if (new FileInfo(path).Length is <= 0 or > 134_217_728)
                    return new(ReadStatus.Unavailable, [], "OCR language data is empty or exceeds 128 MiB.", false);
                // osd is orientation metadata, not a recognition language.
                if (id != "osd") languages.Add(id);
            }
            return new(ReadStatus.Success, languages.Order(StringComparer.Ordinal).ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(ReadStatus.Error, [], error.Message, false); }
    }

    public async ValueTask<TextReadResult> ReadAsync(OcrFrame frame, string language, CancellationToken token)
    {
        var languages = GetLanguages(options, token);
        if (languages.Status != ReadStatus.Success) return new(languages.Status, Detail: languages.Detail);
        if (!languages.Choices.Contains(language, StringComparer.Ordinal)) return new(ReadStatus.Unavailable, Detail: "The selected OCR language is not installed locally.");
        var bitmap = EncodeBitmap(frame);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var output = await _command.RunAsync(options.TesseractExecutablePath, options.TessdataDirectory, language, bitmap, bounded.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (output.ExitCode != 0) return new(ReadStatus.Error, Detail: "Local OCR failed: " + output.Error);
            if (output.Text.Length > TextPredicates.MaximumCharacters) return new(ReadStatus.Unavailable, Detail: "OCR returned more than 64 KiB of text.");
            return new(ReadStatus.Success, output.Text.TrimEnd('\r', '\n', '\f'), frame.Identity);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(ReadStatus.Unavailable, Detail: "Local OCR exceeded its ten-second operation limit."); }
    }

    internal static byte[] EncodeBitmap(OcrFrame frame)
    {
        if (frame.Width is < 1 or > 4096 || frame.Height is < 1 or > 4096 || (long)frame.Width * frame.Height > 1_000_000
            || frame.Bgra.Length != (long)frame.Width * frame.Height * 4) throw new ArgumentException("OCR frame exceeds its bounded region or has an incomplete pixel buffer.");
        var bytes = new byte[checked(54 + frame.Bgra.Length)];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), -frame.Height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), frame.Bgra.Length);
        frame.Bgra.CopyTo(bytes, 54);
        return bytes;
    }
}

internal interface ILocalOcrProcess : IDisposable
{
    bool Start();
    bool HasExited { get; }
    int ExitCode { get; }
    Stream Input { get; }
    TextReader Output { get; }
    TextReader Error { get; }
    void Kill();
    Task WaitForExitAsync(CancellationToken token);
}

internal sealed class LocalOcrProcess(ProcessStartInfo start) : ILocalOcrProcess
{
    private readonly Process _process = new() { StartInfo = start };
    public bool Start() => _process.Start();
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;
    public Stream Input => _process.StandardInput.BaseStream;
    public TextReader Output => _process.StandardOutput;
    public TextReader Error => _process.StandardError;
    public void Kill() => _process.Kill(entireProcessTree: true);
    public Task WaitForExitAsync(CancellationToken token) => _process.WaitForExitAsync(token);
    public void Dispose() => _process.Dispose();
}

internal sealed class LocalOcrCommand(Func<ProcessStartInfo, ILocalOcrProcess>? processFactory = null) : IOcrCommand
{
    public async Task<OcrCommandResult> RunAsync(string executable, string tessdata, string language, byte[] bitmap, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        foreach (var argument in new[] { "stdin", "stdout", "--tessdata-dir", tessdata, "-l", language, "--psm", "6" }) start.ArgumentList.Add(argument);
        start.Environment["OMP_THREAD_LIMIT"] = "1";
        using var process = processFactory?.Invoke(start) ?? new LocalOcrProcess(start);
        token.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("Local OCR could not start.");
        Exception? terminationFailure = null;
        void Kill()
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Exception error)
            {
                // Tree termination can aggregate failures. No termination failure
                // may escape a timer callback or bypass process/pipe draining.
                if (Interlocked.CompareExchange(ref terminationFailure, error, null) is null)
                    Trace.TraceError($"Local OCR termination failed ({error.GetType().Name}, 0x{error.HResult:X8}); "
                        + "retaining the observation permit until the process and pipes close. Close the local OCR process or restart the app if it does not exit.");
            }
        }
        using var registration = token.Register(Kill);
        Task<string>? stdout = null, stderr = null;
        Task? input = null;
        try
        {
            stdout = ReadBoundedAsync(process.Output, TextPredicates.MaximumCharacters + 2, Kill);
            stderr = ReadBoundedAsync(process.Error, 8192, Kill);
            input = WriteInputAsync(process, bitmap, token);
            await Task.WhenAll(input, stdout, stderr, process.WaitForExitAsync(token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        finally
        {
            Kill();
            // Teardown stays in the observation task. The runner cannot dispose
            // the process or release its permit while pipes/process are active.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(input ?? Task.CompletedTask, stdout ?? Task.FromResult(""), stderr ?? Task.FromResult("")).ConfigureAwait(false); }
            catch { }
            await registration.DisposeAsync().ConfigureAwait(false);
            if (terminationFailure is { } failure)
                throw new InvalidOperationException("Local OCR termination failed. Its process and pipes have now closed; check the local OCR installation and process permissions before retrying.", failure);
        }
    }
    private static async Task WriteInputAsync(ILocalOcrProcess process, byte[] bitmap, CancellationToken token)
    {
        try { await process.Input.WriteAsync(bitmap, token).ConfigureAwait(false); }
        finally { process.Input.Close(); }
    }
    private static async Task<string> ReadBoundedAsync(TextReader reader, int maximum, Action kill)
    {
        var text = new StringBuilder(); var buffer = new char[1024];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (read == 0) return text.ToString();
                if (text.Length + read > maximum) throw new InvalidDataException("Local OCR output exceeded its bound.");
                text.Append(buffer, 0, read);
            }
        }
        catch { kill(); throw; }
    }
}
