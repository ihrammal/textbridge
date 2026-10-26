using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AccessibleOCRStudio.Services;

public sealed record OcrSpanResult(string Text, int[] Box);
public sealed record OcrLineResult(string Text, double Confidence, int[] Box, OcrSpanResult[] Words);
public sealed record OcrPageResult(string Text, OcrLineResult[] Lines, int Width, int Height, int Angle)
{
    public IEnumerable<OcrWord> ReviewWords => Lines.SelectMany((line, index) => line.Words.Select(word =>
        new OcrWord(word.Text, line.Confidence, 1, index + 1, word.Box[0], word.Box[1],
            Math.Max(1, word.Box[2] - word.Box[0]), Math.Max(1, word.Box[3] - word.Box[1]))));

    // Preserve decoded logical text and line positions in the existing hybrid
    // PDF merger. Arabic is a line span because upstream word boxes are visual.
    public string ToTsv()
    {
        var text = new StringBuilder("level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n");
        foreach (var word in ReviewWords)
            text.AppendLine(FormattableString.Invariant($"5\t1\t1\t1\t{word.Line}\t1\t{word.Left}\t{word.Top}\t{word.Width}\t{word.Height}\t{word.Confidence}\t{word.Text.Replace('\t', ' ').Replace('\n', ' ')}"));
        return text.ToString();
    }
}

/// <summary>Serializes requests to the bundled, network-disabled OCR worker.</summary>
public sealed class PaddleOcrService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static Process? worker;
    static PaddleOcrService() => AppDomain.CurrentDomain.ProcessExit += (_, _) => StopWorker();

    internal static string[] RequiredModels(string languages, bool rotate)
    {
        var profiles = languages.Split('+');
        var models = new List<string> { "PP-OCRv5_mobile_det" };
        if (profiles.Contains("ara")) models.Add("arabic_PP-OCRv5_mobile_rec");
        if (profiles.Contains("fra")) models.Add("latin_PP-OCRv5_mobile_rec");
        else if (profiles.Contains("eng")) models.Add("en_PP-OCRv5_mobile_rec");
        if (rotate) models.Add("PP-LCNet_x1_0_doc_ori");
        return models.ToArray();
    }

    internal static bool ModelInstalled(string model, string? root = null) =>
        File.Exists(Path.Combine(root ?? AppContext.BaseDirectory, "paddle", "models", model, "inference.json")) &&
        File.Exists(Path.Combine(root ?? AppContext.BaseDirectory, "paddle", "models", model, "inference.pdiparams"));

    internal static string[] InstalledProfiles(string? root = null) => new[] {
        ("ara", "arabic_PP-OCRv5_mobile_rec"), ("eng", "en_PP-OCRv5_mobile_rec"), ("fra", "latin_PP-OCRv5_mobile_rec")
    }.Where(item => ModelInstalled(item.Item2, root)).Select(item => item.Item1).ToArray();

    public async Task<OcrPageResult> RecognizeAsync(string image, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var missing = RequiredModels(settings.OcrLanguages, settings.AutoRotate).Where(model => !ModelInstalled(model)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(UiLanguage.T("The selected language model is not installed. Choose an installed OCR language or rerun Custom setup to add it."));
        var response = await RequestAsync(new { image = Path.GetFullPath(image), languages = settings.OcrLanguages, rotate = settings.AutoRotate, preserveLayout = settings.PreserveTextLayout }, cancellationToken);
        return response.Deserialize<OcrPageResult>(Json) ?? throw new InvalidOperationException("The offline OCR engine returned no result.");
    }

    private static async Task<JsonElement> RequestAsync(object request, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (worker is null || worker.HasExited)
            {
                StopWorker();
                var folder = Path.Combine(AppContext.BaseDirectory, "paddle");
                var executable = Path.Combine(folder, "python", "python.exe");
                var script = Path.Combine(folder, "worker.py");
                if (!File.Exists(executable) || !File.Exists(script))
                    throw new InvalidOperationException("The bundled PaddleOCR runtime is missing. Reinstall TextBridge.");
                var info = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = folder
                };
                info.ArgumentList.Add("-I"); info.ArgumentList.Add("-B"); info.ArgumentList.Add("-u"); info.ArgumentList.Add(script);
                worker = Process.Start(info) ?? throw new InvalidOperationException("Unable to start local PaddleOCR.");
                // Drain diagnostics continuously so native-library logs cannot
                // fill a pipe and deadlock recognition. Documents stay local.
                worker.ErrorDataReceived += (_, _) => { };
                worker.BeginErrorReadLine();
            }
            await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await worker.StandardInput.FlushAsync();
            var line = await worker.StandardOutput.ReadLineAsync().WaitAsync(cancellationToken);
            if (line is null) throw new InvalidOperationException("The offline OCR engine stopped unexpectedly. Try again or reinstall TextBridge.");
            using var response = JsonDocument.Parse(line);
            if (!response.RootElement.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException("Offline recognition failed: " + response.RootElement.GetProperty("error").GetString());
            return response.RootElement.GetProperty("result").Clone();
        }
        catch (OperationCanceledException) { StopWorker(); throw; }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            StopWorker();
            throw new InvalidOperationException("The offline OCR engine could not return a result. Try again.", exception);
        }
        finally { Gate.Release(); }
    }

    private static void StopWorker()
    {
        try { if (worker is { HasExited: false }) worker.Kill(entireProcessTree: true); } catch { }
        worker?.Dispose(); worker = null;
    }
}
