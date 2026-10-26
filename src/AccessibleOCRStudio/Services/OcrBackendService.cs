namespace AccessibleOCRStudio.Services;

public sealed class OcrBackendService
{
    public static bool PaddleInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory,"paddle","python","python.exe"));

    public static string DefaultEngine
    {
        get
        {
            try
            {
                var selected=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ocr-engine.txt")).Trim();
                if(selected is "windows" or "paddle") return selected;
            }
            catch { }
            return PaddleInstalled ? "paddle" : "windows";
        }
    }

    public static string ResolveEngine(AppSettings settings) => settings.OcrEngine switch
    {
        "windows" => "windows",
        "paddle" => PaddleInstalled ? "paddle" : DefaultEngine,
        _ => DefaultEngine
    };

    public Task<OcrPageResult> RecognizeAsync(string image, AppSettings settings, CancellationToken cancellationToken = default)
        => ResolveEngine(settings)=="windows"
            ? new WindowsOcrService().RecognizeAsync(image,settings,cancellationToken)
            : new PaddleOcrService().RecognizeAsync(image,settings,cancellationToken);

    public Task CreatePdfPageAsync(string image,string output,OcrPageResult result)
        => Task.Run(()=>SearchablePdfWriter.Write(image,output,result));
}
