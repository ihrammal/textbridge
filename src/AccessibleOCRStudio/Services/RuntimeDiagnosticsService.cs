namespace AccessibleOCRStudio.Services;

public sealed record RuntimeDiagnosticResult(bool CoreOcrReady, string Summary, string Details);

public sealed class RuntimeDiagnosticsService
{
    public RuntimeDiagnosticResult Check(AppSettings? settings = null)
    {
        settings ??= AppSettings.Load();
        var selected=OcrBackendService.ResolveEngine(settings);
        var baseDir = AppContext.BaseDirectory;
        var items = new List<(string Name, bool Ok, string Path)>();
        void Add(string name, string bundled, string fallback)
        {
            var p = Path.Combine(baseDir, bundled);
            var ok = File.Exists(p) || CommandOnPath(fallback);
            items.Add((name, ok, File.Exists(p) ? p : fallback));
        }
        if(selected=="paddle")
        {
            items.Add(("PaddleOCR runtime", File.Exists(Path.Combine(baseDir,"paddle","python","python.exe")), Path.Combine(baseDir,"paddle","python","python.exe")));
            items.Add(("PaddleOCR worker", File.Exists(Path.Combine(baseDir,"paddle","worker.py")), Path.Combine(baseDir,"paddle","worker.py")));
        }
        else
        {
            var available=WindowsOcrService.AvailableProfiles;
            foreach(var profile in settings.OcrLanguages.Split('+'))
                items.Add(($"Windows OCR language {profile}",available.Contains(profile),"Windows language packs"));
        }
        Add("PDF renderer", Path.Combine("pdf","pdftoppm.exe"), "pdftoppm.exe");
        Add("PDF text extractor", Path.Combine("pdf","pdftotext.exe"), "pdftotext.exe");
        Add("PDF image inspector", Path.Combine("pdf","pdfimages.exe"), "pdfimages.exe");
        Add("PDF page information", Path.Combine("pdf","pdfinfo.exe"), "pdfinfo.exe");
        Add("PDF merger", Path.Combine("pdf","pdfunite.exe"), "pdfunite.exe");
        Add("Image preprocessing", Path.Combine("image", "magick.exe"), "magick.exe");
        foreach (var model in selected=="paddle" ? PaddleOcrService.RequiredModels(settings.OcrLanguages, settings.AutoRotate) : Array.Empty<string>())
        {
            var directory = Path.Combine(baseDir,"paddle","models",model);
            items.Add(($"OCR model {model}", File.Exists(Path.Combine(directory,"inference.json")) && File.Exists(Path.Combine(directory,"inference.pdiparams")), directory));
        }
        var core = items.Where(i => i.Name.StartsWith("PaddleOCR") || i.Name.StartsWith("OCR model") || i.Name.StartsWith("Windows OCR language")).All(i => i.Ok);
        var details = "Selected engine: "+(selected=="windows"?"Windows OCR":"PaddleOCR")+Environment.NewLine+string.Join(Environment.NewLine, items.Select(i => $"{(i.Ok ? "Available" : "Missing")}: {i.Name}. {i.Path}"));
        return new(core, core ? "Local OCR runtime is ready." : "Local OCR runtime is incomplete. Open Help, Runtime Diagnostics for details.", details);
    }
    private static bool CommandOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(d => File.Exists(Path.Combine(d.Trim('"'), name)));
    }
}
