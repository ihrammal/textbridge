using System.Diagnostics;
using System.Text;
using System.Windows.Media.Imaging;

namespace AccessibleOCRStudio.Services;

public sealed class LocalOcrService
{
    private readonly ImagePreprocessingService _preprocessing = new();
    public async Task<string> RecognizeAsync(string input, AppSettings settings, IProgress<string>? progress = null,
        IProgress<OcrProgress>? updates = null, bool includePageHeadings = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ext = Path.GetExtension(input).ToLowerInvariant();
        if (ext == ".pdf") return await RecognizePdfAsync(input, settings, progress, updates, includePageHeadings, cancellationToken);
        progress?.Report("Recognizing image.");
        updates?.Report(new(OcrStage.Recognizing, 1, 1, 0));
        var text = await RecognizeImageAsync(input, settings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var chunk = OcrProgress.Chunk(text, 1, 1, includePageHeadings, settings.MessageLanguage);
        updates?.Report(new(OcrStage.PageCompleted, 1, 1, 1, chunk, text));
        return chunk;
    }


    public async Task<RecognitionDocument> RecognizeWithConfidenceAsync(string input, AppSettings settings, IProgress<string>? progress = null,
        IProgress<OcrProgress>? updates = null, bool includePageHeadings = false, CancellationToken cancellationToken = default)
    {
        var document = new RecognitionDocument();
        if (Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            document.PlainText = await RecognizeAsync(input, settings, progress, updates, includePageHeadings, cancellationToken);
        else
        {
            updates?.Report(new(OcrStage.Recognizing, 1, 1, 0));
            var prepared = await _preprocessing.PrepareAsync(input, settings, cancellationToken);
            try
            {
                var result = await new OcrBackendService().RecognizeAsync(prepared.Path, settings, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                document.PlainText = OcrProgress.Chunk(result.Text, 1, 1, includePageHeadings, settings.MessageLanguage);
                document.ContentStart = includePageHeadings ? document.PlainText.IndexOf(Environment.NewLine, StringComparison.Ordinal) + Environment.NewLine.Length : 0;
                document.Words.AddRange(result.ReviewWords);
                updates?.Report(new(OcrStage.PageCompleted, 1, 1, 1, document.PlainText, result.Text));
            }
            finally { if (prepared.Temporary) try { File.Delete(prepared.Path); } catch { } }
        }
        document.Blocks.AddRange(new DocumentStructureService().Analyze(document.PlainText));
        return document;
    }

    private async Task<string> RecognizePdfAsync(string input, AppSettings settings, IProgress<string>? progress,
        IProgress<OcrProgress>? updates, bool headings, CancellationToken cancellationToken)
    {
        updates?.Report(new(OcrStage.Inspecting, 0, 0, 0));
        var info = NewProcess(FindExecutable(Path.Combine(AppContext.BaseDirectory, "pdf", "pdfinfo.exe"), "pdfinfo"));
        info.ArgumentList.Add(input);
        var metadata = await RunCheckedAsync(info, "Unable to inspect PDF page count.", true, cancellationToken);
        var match = System.Text.RegularExpressions.Regex.Match(metadata, @"(?m)^Pages:\s+(\d+)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var total) || total <= 0)
            throw new InvalidOperationException("Unable to determine the PDF page count.");
        updates?.Report(new(OcrStage.Inspecting, 1, total, 0));
        var tempDir = Path.Combine(Path.GetTempPath(), "AccessibleOCRStudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var renderer = FindExecutable(Path.Combine(AppContext.BaseDirectory, "pdf", "pdftoppm.exe"), "pdftoppm");
            var result = new StringBuilder();
            for (var i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text;
                // Inspect one page at a time: do not make the first result wait
                // for text/image/layout extraction of the entire large document.
                string? digitalText = null;
                HashSet<int>? imagePages = null;
                IReadOnlyList<PdfHybridTextService.Page>? layout = null;
                updates?.Report(new(OcrStage.Inspecting, i + 1, total, i));
                if (settings.PreferDigitalPdfText)
                {
                    digitalText = await TryExtractDigitalPdfTextAsync(input, cancellationToken, i + 1);
                    if (HasText(digitalText))
                    {
                        progress?.Report("Digital PDF text detected. Checking this page for scanned content.");
                        imagePages = await TryGetPdfImagePagesAsync(input, cancellationToken, i + 1);
                        if (imagePages is null || imagePages.Count > 0)
                            layout = await TryExtractPdfLayoutAsync(input, cancellationToken, i + 1);
                    }
                }
                var hasDigital = digitalText is not null && HasText(digitalText);
                if (hasDigital && imagePages is not null && !imagePages.Contains(i + 1))
                {
                    progress?.Report($"Using digital PDF text for page {i + 1} of {total}. Digital PDF text detected. OCR was not required.");
                    text = digitalText!;
                }
                else
                {
                    var prefix = Path.Combine(tempDir, $"page-{i + 1}");
                    var image = prefix + ".png";
                    try
                    {
                        updates?.Report(new(OcrStage.Rendering, i + 1, total, i));
                        var psi = NewProcess(renderer);
                        foreach (var argument in new[] { "-f", (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "-l", (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "-singlefile", "-r", "300", "-cropbox", "-png", input, prefix })
                            psi.ArgumentList.Add(argument);
                        await RunCheckedAsync(psi, "Unable to render the PDF page for OCR.", cancellationToken: cancellationToken);
                        if (!File.Exists(image)) throw new InvalidOperationException("The PDF page was not rendered.");
                        updates?.Report(new(OcrStage.Recognizing, i + 1, total, i));
                        if (hasDigital && layout is not null && layout.Count > 0 && layout[0].Lines.Count > 0)
                        {
                            progress?.Report($"Recognizing scanned content and preserving digital PDF text for page {i + 1} of {total}.");
                            text = await RecognizeHybridPageAsync(image, layout[0], digitalText!, settings, cancellationToken);
                        }
                        else
                        {
                            progress?.Report($"Recognizing page {i + 1} of {total}.");
                            text = await RecognizeImageAsync(image, settings, cancellationToken);
                        }
                    }
                    finally
                    {
                        foreach (var path in new[] { image, Path.ChangeExtension(image, ".upright.png") })
                            try { File.Delete(path); } catch { }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = OcrProgress.Chunk(text, i + 1, total, headings, settings.MessageLanguage);
                result.Append(chunk);
                updates?.Report(new(OcrStage.PageCompleted, i + 1, total, i + 1, chunk, text));
                await Task.Yield();
            }
            return result.ToString();
        }
        finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { } }
    }

    private static bool HasText(string text) => text.Any(char.IsLetterOrDigit);

    private async Task<string> TryExtractDigitalPdfTextAsync(string input, CancellationToken cancellationToken, int page)
    {
        try
        {
            var exe = FindExecutable(Path.Combine(AppContext.BaseDirectory, "pdf", "pdftotext.exe"), "pdftotext");
            var psi = NewProcess(exe);
            psi.ArgumentList.Add("-layout"); psi.ArgumentList.Add("-enc"); psi.ArgumentList.Add("UTF-8");
            psi.ArgumentList.Add("-cropbox");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(input); psi.ArgumentList.Add("-");
            return await RunCheckedAsync(psi, "Digital PDF text extraction failed.", true, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch { return string.Empty; }
    }

    private async Task<HashSet<int>?> TryGetPdfImagePagesAsync(string input, CancellationToken cancellationToken, int page)
    {
        try
        {
            var exe = FindExecutable(Path.Combine(AppContext.BaseDirectory, "pdf", "pdfimages.exe"), "pdfimages");
            var psi = NewProcess(exe);
            psi.ArgumentList.Add("-list"); psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture)); psi.ArgumentList.Add(input);
            var output = await RunCheckedAsync(psi, "Unable to inspect PDF images.", true, cancellationToken);
            if (!output.Contains("page", StringComparison.Ordinal) || !output.Contains("type", StringComparison.Ordinal)) return null;
            var pages = new HashSet<int>();
            foreach (var row in output.Split('\n'))
            {
                var columns = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length >= 3 && int.TryParse(columns[0], out var imagePage) && int.TryParse(columns[1], out _)) pages.Add(imagePage);
            }
            return pages;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // Unknown image coverage must not bypass OCR.
    }

    private async Task<IReadOnlyList<PdfHybridTextService.Page>?> TryExtractPdfLayoutAsync(string input, CancellationToken cancellationToken, int page)
    {
        try
        {
            var exe = FindExecutable(Path.Combine(AppContext.BaseDirectory, "pdf", "pdftotext.exe"), "pdftotext");
            var psi = NewProcess(exe);
            psi.ArgumentList.Add("-bbox-layout"); psi.ArgumentList.Add("-cropbox");
            psi.ArgumentList.Add("-enc"); psi.ArgumentList.Add("UTF-8");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(input); psi.ArgumentList.Add("-");
            var layout = PdfHybridTextService.ParseLayout(await RunCheckedAsync(psi, "Unable to extract PDF text positions.", true, cancellationToken));
            var info = NewProcess(FindExecutable(Path.Combine(AppContext.BaseDirectory, "pdf", "pdfinfo.exe"), "pdfinfo"));
            info.ArgumentList.Add("-f"); info.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add("-l"); info.ArgumentList.Add(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add(input);
            var metadata = await RunCheckedAsync(info, "Unable to inspect PDF page rotation.", true, cancellationToken);
            var rotations = System.Text.RegularExpressions.Regex.Matches(metadata, @"Page\s+(\d+)\s+rot:\s+(-?\d+)")
                .ToDictionary(match => int.Parse(match.Groups[1].Value), match => ((int.Parse(match.Groups[2].Value) % 360) + 360) % 360);
            if (rotations.Count != layout.Count) return null;
            return layout.Select((item, i) => item with { Rotation = rotations[i + page] }).ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // Full-page OCR remains available if layout extraction fails.
    }

    private async Task<string> RecognizeHybridPageAsync(string image, PdfHybridTextService.Page native, string nativeText, AppSettings settings, CancellationToken cancellationToken)
    {
        BitmapSource bitmap;
        using (var stream = File.OpenRead(image))
            bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        // PDF rotation is known, so undo it before OCR rather than depending on
        // orientation detection on a sparse page. Keep deskew disabled here so
        // native text and OCR geometry remain aligned.
        if (native.Rotation != 0)
        {
            bitmap = new TransformedBitmap(bitmap, new System.Windows.Media.RotateTransform(-native.Rotation));
            var upright = Path.ChangeExtension(image, ".upright.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(upright)) encoder.Save(output);
            image = upright;
        }
        var pixelWidth = bitmap.PixelWidth;
        var pixelHeight = bitmap.PixelHeight;
        // Known PDF rotation has already been corrected above. Prevent the
        // classifier from changing the canvas used to align native PDF text.
        var uprightSettings = new AppSettings { OcrLanguages = settings.OcrLanguages, OcrEngine = settings.OcrEngine, PreserveTextLayout = settings.PreserveTextLayout, AutoRotate = false };
        var recognized = await new OcrBackendService().RecognizeAsync(image, uprightSettings, cancellationToken);
        return PdfHybridTextService.Merge(native, nativeText, recognized.ToTsv(), pixelWidth, pixelHeight);
    }

    private async Task<string> RecognizeImageAsync(string input, AppSettings settings, CancellationToken cancellationToken)
    {
        var prepared = await _preprocessing.PrepareAsync(input, settings, cancellationToken);
        try { return (await new OcrBackendService().RecognizeAsync(prepared.Path, settings, cancellationToken)).Text; }
        finally { if (prepared.Temporary) try { File.Delete(prepared.Path); } catch { } }
    }

    private static ProcessStartInfo NewProcess(string exe) => new(exe)
    { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, UseShellExecute = false, CreateNoWindow = true };

    private static async Task<string> RunCheckedAsync(ProcessStartInfo psi, string message, bool returnStdout = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(message);
        using var stop = cancellationToken.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } });
        var stdoutTask = p.StandardOutput.ReadToEndAsync(); var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(cancellationToken); var stdout = await stdoutTask; var stderr = await stderrTask;
        if (p.ExitCode != 0) throw new InvalidOperationException($"{message}\n{stderr}".Trim());
        return returnStdout ? stdout : string.Empty;
    }

    private static string FindExecutable(string bundled, string fallback) => File.Exists(bundled) ? bundled : fallback;
}

