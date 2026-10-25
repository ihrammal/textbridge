using System.Text.Json;

namespace AccessibleOCRStudio.Services;

public sealed class AppSettings
{
    public string GitHubRepositoryUrl { get; set; } = "https://github.com/ihrammal/textbridge";
    public string InterfaceLanguage { get; set; } = "auto";
    public string MessageLanguage { get; set; } = "auto";
    public bool PlayStartupSound { get; set; } = true;
    public bool PlayExitSound { get; set; } = true;
    public bool ShowWelcomeMessage { get; set; } = true;
    public bool AnnounceOcrProgress { get; set; } = true;
    public bool ShowStatusBar { get; set; } = false;
    public string DocumentFontFamily { get; set; } = "Segoe UI";
    public double DocumentFontSize { get; set; } = 18;
    public double InterfaceFontSize { get; set; } = 14;
    public bool DocumentBold { get; set; } = false;
    public string ReadingColors { get; set; } = "system";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool ScreenReaderOptimization { get; set; } = true;
    public bool AccessibleReadingMode { get; set; } = true;
    public bool FollowWindowsHighContrast { get; set; } = true;
    public bool PreserveTextLayout { get; set; } = false;
    public bool PreferDigitalPdfText { get; set; } = true;
    public bool Deskew { get; set; } = true;
    public bool AutoRotate { get; set; } = true;
    public bool Grayscale { get; set; } = false;
    public string OcrLanguages { get; set; } = "ara+eng+fra";
    public string OcrEngine { get; set; } = "auto";
    public string OcrMode { get; set; } = "accurate";
    public bool OfflineOnlyMode { get; set; } = true;
    public bool AllowApiDocumentContent { get; set; } = false;
    public bool AllowApiOcrCorrection { get; set; } = false;
    public bool AllowImageDescription { get; set; } = false;
    public bool AllowTableReconstruction { get; set; } = false;
    public double LowConfidenceThreshold { get; set; } = 70;
    public string GeminiModel { get; set; } = "gemini-3.5-flash-lite";
    public string GroqModel { get; set; } = "openai/gpt-oss-20b";
    public string OpenAiModel { get; set; } = OpenAiCorrectionService.DefaultModel;
    public string ApiProvider { get; set; } = "None";
    public string UpdateFeedUrl { get; set; } = "https://api.github.com/repos/ihrammal/textbridge/releases/latest";

    private static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextBridge");

    private static string LegacyAppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Accessible OCR Studio");

    private static string SettingsPath => Path.Combine(AppDataRoot, "settings.json");

    public static void MigrateFromLegacy(string legacyDirectory, string newDirectory, string fileName)
    {
        var legacyFile = Path.Combine(legacyDirectory, fileName);
        if (!File.Exists(legacyFile)) return;
        var newFile = Path.Combine(newDirectory, fileName);
        if (File.Exists(newFile)) return;
        Directory.CreateDirectory(newDirectory);
        File.Copy(legacyFile, newFile);
    }

    public static AppSettings Load(string? path = null)
    {
        try
        {
            if (path is null)
            {
                path = SettingsPath;
                MigrateFromLegacy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clear Read"), AppDataRoot, "settings.json");
                MigrateFromLegacy(LegacyAppDataRoot, AppDataRoot, "settings.json");
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            if (string.IsNullOrWhiteSpace(settings.UpdateFeedUrl)) settings.UpdateFeedUrl = new AppSettings().UpdateFeedUrl;
            if (string.IsNullOrWhiteSpace(settings.GitHubRepositoryUrl)) settings.GitHubRepositoryUrl = new AppSettings().GitHubRepositoryUrl;
            // Use the canonical repository after its rename; preserve custom feeds.
            if (string.Equals(settings.UpdateFeedUrl.TrimEnd('/'), "https://api.github.com/repos/ihrammal/clear-read/releases/latest", StringComparison.OrdinalIgnoreCase))
                settings.UpdateFeedUrl = new AppSettings().UpdateFeedUrl;
            if (string.Equals(settings.GitHubRepositoryUrl.TrimEnd('/'), "https://github.com/ihrammal/clear-read", StringComparison.OrdinalIgnoreCase))
                settings.GitHubRepositoryUrl = new AppSettings().GitHubRepositoryUrl;
            return settings;
        }
        catch
        {
            var settings=new AppSettings();
            if(OcrBackendService.ResolveEngine(settings)=="windows")
            {
                var profiles=WindowsOcrService.AvailableProfiles;
                if(profiles.Count>0)settings.OcrLanguages=string.Join("+",profiles);
            }
            if (OcrBackendService.ResolveEngine(settings) == "paddle")
            {
                var profiles = PaddleOcrService.InstalledProfiles();
                if (profiles.Length > 0) settings.OcrLanguages = string.Join("+", profiles);
            }
            return settings;
        }
    }
    public void Save(string? path = null)
    {
        path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}





