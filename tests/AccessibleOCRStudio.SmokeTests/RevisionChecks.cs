using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AccessibleOCRStudio;
using AccessibleOCRStudio.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

internal static class RevisionChecks
{
    internal static void Run(string temp, Action<bool,string> check)
    {
        check(PaddleOcrService.RequiredModels("ara", false).SequenceEqual(new[] { "PP-OCRv5_mobile_det", "arabic_PP-OCRv5_mobile_rec" }), "Arabic-only setup needs no English or French model");
        check(PaddleOcrService.RequiredModels("eng+fra", true).SequenceEqual(new[] { "PP-OCRv5_mobile_det", "latin_PP-OCRv5_mobile_rec", "PP-LCNet_x1_0_doc_ori" }), "French combined recognition uses the Latin model and optional orientation model");
        var modelRoot = Path.Combine(temp, "partial-models");
        var modelDir = Path.Combine(modelRoot, "paddle", "models", "en_PP-OCRv5_mobile_rec");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "inference.json"), "{}");
        check(PaddleOcrService.InstalledProfiles(modelRoot).Length == 0, "Incomplete language model is never offered as installed");
        File.WriteAllText(Path.Combine(modelDir, "inference.pdiparams"), "fixture");
        check(PaddleOcrService.InstalledProfiles(modelRoot).SequenceEqual(new[] { "eng" }), "Custom English-only installation reports exactly its installed language");
        var clearRead = Path.Combine(temp, "Clear Read");
        var textBridge = Path.Combine(temp, "TextBridge");
        Directory.CreateDirectory(clearRead);
        File.WriteAllText(Path.Combine(clearRead, "settings.json"), "{\"OcrLanguages\":\"fra\"}");
        AppSettings.MigrateFromLegacy(clearRead, textBridge, "settings.json");
        check(AppSettings.Load(Path.Combine(textBridge, "settings.json")).OcrLanguages == "fra", "Clear Read settings migrate to TextBridge");
        File.WriteAllText(Path.Combine(textBridge, "settings.json"), "{\"OcrLanguages\":\"eng\"}");
        AppSettings.MigrateFromLegacy(clearRead, textBridge, "settings.json");
        check(AppSettings.Load(Path.Combine(textBridge, "settings.json")).OcrLanguages == "eng", "Migration preserves existing TextBridge preferences");
        var oldKeys = Path.Combine(clearRead, "credentials");
        var newKeys = Path.Combine(textBridge, "credentials");
        new CredentialService(oldKeys).Save("OpenAI", "legacy-key");
        new CredentialService(newKeys).Save("Groq", "current-key");
        CredentialService.MigrateFromLegacy(oldKeys, newKeys);
        check(new CredentialService(newKeys).Load("OpenAI") == "legacy-key" && new CredentialService(newKeys).Load("Groq") == "current-key", "Protected keys migrate into an existing TextBridge credential folder");
        new CredentialService(oldKeys).Save("Groq", "older-key");
        CredentialService.MigrateFromLegacy(oldKeys, newKeys);
        check(new CredentialService(newKeys).Load("Groq") == "current-key", "Credential migration preserves newer saved keys");
        check(new AppSettings().UpdateFeedUrl == "https://api.github.com/repos/ihrammal/textbridge/releases/latest", "Default update feed uses the owner-approved TextBridge GitHub releases");
        var legacyUpdate = Path.Combine(temp, "empty-update-settings.json");
        File.WriteAllText(legacyUpdate, "{\"UpdateFeedUrl\":\"\"}");
        check(AppSettings.Load(legacyUpdate).UpdateFeedUrl == new AppSettings().UpdateFeedUrl, "Existing empty update feed migrates to the approved public feed");
        File.WriteAllText(legacyUpdate, "{\"UpdateFeedUrl\":\"https://api.github.com/repos/ihrammal/clear-read/releases/latest\",\"GitHubRepositoryUrl\":\"https://github.com/ihrammal/clear-read\"}");
        var renamedRepository = AppSettings.Load(legacyUpdate);
        check(renamedRepository.UpdateFeedUrl == new AppSettings().UpdateFeedUrl && renamedRepository.GitHubRepositoryUrl == new AppSettings().GitHubRepositoryUrl,
            "Saved Clear Read repository and feed URLs migrate to the canonical TextBridge repository");
        File.WriteAllText(legacyUpdate, "{\"UpdateFeedUrl\":\"https://updates.example/custom\",\"GitHubRepositoryUrl\":\"https://github.com/example/custom\"}");
        var customRepository = AppSettings.Load(legacyUpdate);
        check(customRepository.UpdateFeedUrl == "https://updates.example/custom" && customRepository.GitHubRepositoryUrl == "https://github.com/example/custom",
            "Repository rename migration preserves custom update feeds and reporting destinations");
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "ar" });
        var window = new MainWindow();
        var editor = (TextBox)window.FindName("DocumentText");
        editor.Text = "Ready\nEnglish Français العربية";
        UiLanguage.Apply(window);
        var menu = (Menu)window.FindName("ApplicationMenu");
        check(window.FlowDirection == FlowDirection.RightToLeft && ((MenuItem)menu.Items[0]).Header?.ToString() == "_ملف", "Arabic interface translates menus and uses right-to-left layout");
        check(editor.Text == "Ready\nEnglish Français العربية", "Interface translation never translates document text");
        check(UiLanguage.T("Saved example.txt.") == "تم حفظ example.txt.", "Arabic status translation preserves filenames");
        check(UiLanguage.T("Word to check 2 of 3: test. Recognition confidence: 40 percent.").Contains("test"), "Arabic review status preserves recognized word");
        var settings = new SettingsWindow(new AppSettings { InterfaceLanguage = "ar" });
        UiLanguage.Apply(settings);
        check(settings.Title == "إعدادات TextBridge" && ((ComboBox)settings.FindName("InterfaceLanguage")).SelectedIndex == 1, "Arabic settings preserve the selected interface language");
        var privacy = new ApiSettingsWindow(new AppSettings { ApiProvider = "None" });
        UiLanguage.Apply(privacy);
        var pending = (AppSettings)typeof(ApiSettingsWindow).GetMethod("PendingSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(privacy, null)!;
        check(pending.ApiProvider == "None", "Translated provider labels retain stable provider identifiers");
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
        UiLanguage.Apply(window);
        check(((MenuItem)menu.Items[0]).Header?.ToString() == "_File" && window.FlowDirection == FlowDirection.LeftToRight, "Interface switches back to English without restarting");
        var top = menu.Items.OfType<MenuItem>().Select(i => i.Header.ToString()).ToList();
        check(top.Distinct().Count() == top.Count && !top.Contains("E_xport") && !top.Contains("_Accessibility") && top.Contains("_Settings"), "Export and accessibility commands have a single menu location");
        var recognize = menu.Items.OfType<MenuItem>().Single(item => item.Header.ToString()!.Replace("_", "") == "Recognize");
        var languageMenu = recognize.Items.OfType<MenuItem>().Single(item => item.HasItems);
        check(languageMenu.Items.OfType<MenuItem>().Select(item => item.Tag?.ToString()).Distinct().Count() == 7,
            "Recognize menu offers every Settings language combination without duplicates");
        var repeated = new RecognitionDocument { PlainText = "test test test" };
        repeated.Words.Add(new OcrWord("test", 95, 1, 1, 0, 0, 1, 1));
        repeated.Words.Add(new OcrWord("test", 20, 1, 1, 0, 0, 1, 1));
        repeated.Words.Add(new OcrWord("test", 30, 1, 1, 0, 0, 1, 1));
        check(repeated.ReviewLocations(repeated.PlainText, 70).Select(w => w.Start).SequenceEqual(new[] { 5, 10 }), "Review selects distinct repeated low-confidence word occurrences");
        check(repeated.ReviewLocations("edited test test", 70).Count == 0, "Review refuses stale locations after document editing");
        editor.Text = repeated.PlainText;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_lastRecognition", flags)!.SetValue(window, repeated);
        var move = typeof(MainWindow).GetMethod("MoveLowConfidence", flags)!;
        move.Invoke(window, new object[] { -1 });
        check(editor.SelectionStart == 10 && editor.SelectedText == "test", "Previous review starts at the last flagged word");
        move.Invoke(window, new object[] { 1 });
        check(editor.SelectionStart == 5, "Next review wraps to the first flagged word");
        editor.Text = "";
        // Render actual WPF controls for visual review, without changing saved user preferences.
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "ar" });
        UiLanguage.Apply(window);
        Render(window, "artifacts/revision-ar-main.png", 1100, 720);
        Render(settings, "artifacts/revision-ar-settings.png", 720, 600);
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
        UiLanguage.Apply(window);
        var text = "  Heading\n\nمرحبا  بالعالم\nÉcole   française\t12";
        var export = new ExportService();
        var docx = Path.Combine(temp, "layout.docx");
        export.SaveAccessibleDocx(docx, text, preserveLayout:true);
        using (var doc = WordprocessingDocument.Open(docx,false))
        {
            var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Skip(1).Select(p=>p.InnerText).ToArray();
            check(paragraphs.SequenceEqual(text.Split('\n')), "Layout-preserving Word export retains blank lines, indentation, Arabic and accents");
        }
        var html = Path.Combine(temp, "layout.html");
        export.SaveHtmlAsync(html,text,preserveLayout:true).GetAwaiter().GetResult();
        check(File.ReadAllText(html).Contains("white-space:pre-wrap") && WebUtility.HtmlDecode(File.ReadAllText(html)).Contains("École   française"), "Layout-preserving HTML export retains spacing");
        UpdateChecks(temp,check).GetAwaiter().GetResult();
    }
    private static void Render(Window window,string path,int width,int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root=(FrameworkElement)window.Content;
        if (root is System.Windows.Controls.Panel panel) panel.Background = System.Windows.SystemColors.WindowBrush;
        root.Measure(new Size(width,height)); root.Arrange(new Rect(0,0,width,height)); root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(path);encoder.Save(output);
    }
    private static async Task UpdateChecks(string temp, Action<bool,string> check)
    {
        var userUpdate = UpdateInstaller.CreateStartInfo("installer.exe", temp, true);
        var adminUpdate = UpdateInstaller.CreateStartInfo("installer.exe", temp, false);
        check(userUpdate.ArgumentList.Contains("/CURRENTUSER") && userUpdate.Verb != "runas" && !userUpdate.ArgumentList.Contains("/ALLUSERS"), "Per-user updates preserve scope without forcing administrator elevation");
        check(adminUpdate.ArgumentList.Contains("/ALLUSERS") && adminUpdate.Verb == "runas" && adminUpdate.ArgumentList.Contains("/NORESTART"), "Machine-wide updates request elevation and never restart Windows");
        var releaseJson = System.Text.Json.JsonSerializer.Serialize(new { tag_name = "v1.1.1", draft = false, prerelease = false,
            assets = new[] { new { name = "TextBridge-Setup-1.1.1.exe", browser_download_url = "https://github.com/ihrammal/textbridge/releases/download/v1.1.1/TextBridge-Setup-1.1.1.exe", digest = "sha256:" + new string('a', 64) } } });
        var releaseUpdate = UpdateService.Parse(releaseJson, "1.1.0");
        check(releaseUpdate.UpdateAvailable && releaseUpdate.LatestVersion == "1.1.1" && releaseUpdate.InstallerUrl!.EndsWith("TextBridge-Setup-1.1.1.exe"), "Installed 1.1.0 clients detect the 1.1.1 GitHub installer and digest");
        check(!UpdateService.Parse(releaseJson.Replace("\"prerelease\":false", "\"prerelease\":true"), "1.1.0").UpdateAvailable, "Startup updater never offers a preview release");
        var correctedJson = releaseJson.Replace("1.1.1", "1.1.0.1");
        var correction = UpdateService.Parse(correctedJson, "1.1.0");
        check(correction.UpdateAvailable && correction.LatestVersion == "1.1.0.1" && correction.InstallerUrl!.EndsWith("TextBridge-Setup-1.1.0.1.exe"), "Original 1.1.0 version detects its corrected internal build");
        check(!UpdateService.Parse(correctedJson, "1.1.0.1").UpdateAvailable, "Corrected build does not repeatedly offer itself");
        check(UpdateService.Parse(correctedJson.Replace("1.1.0.1", "1.1.0.2"), "1.1.0.1").UpdateAvailable, "A later correction of the same displayed version remains discoverable");
        check(!UpdateService.Parse(correctedJson.Replace("\"prerelease\":false", "\"prerelease\":true"), "1.1.0").UpdateAvailable, "Unvalidated corrected builds are never offered as stable updates");
        var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version!;
        check(assemblyVersion.ToString(3) == "1.1.0" && assemblyVersion.ToString(4) == "1.1.0.1", "Displayed version stays 1.1.0 while update checks identify corrected build 1");
        var missingBuildAsset = false;
        try { UpdateService.Parse(correctedJson.Replace("TextBridge-Setup-1.1.0.1.exe", "TextBridge-Setup-1.1.0.exe"), "1.1.0"); }
        catch (InvalidDataException) { missingBuildAsset = true; }
        check(missingBuildAsset, "A build revision requires the exact installer asset expected by original clients");
        // Exercise the actual previously installed client without launching it or using the network.
        var originalClient = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TextBridge", "TextBridge.dll");
        if (File.Exists(originalClient) && System.Diagnostics.FileVersionInfo.GetVersionInfo(originalClient).FileVersion == "1.1.0.0")
        {
            var context = new System.Runtime.Loader.AssemblyLoadContext("TextBridge-original-update-compatibility", isCollectible: true);
            try
            {
                var originalAssembly = context.LoadFromAssemblyPath(originalClient);
                var parser = originalAssembly.GetType("AccessibleOCRStudio.Services.UpdateService")!.GetMethod("Parse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                var originalResult = parser.Invoke(null, new object[] { correctedJson, "1.1.0" })!;
                var resultType = originalResult.GetType();
                check((bool)resultType.GetProperty("UpdateAvailable")!.GetValue(originalResult)! &&
                    ((string)resultType.GetProperty("InstallerUrl")!.GetValue(originalResult)!).EndsWith("TextBridge-Setup-1.1.0.1.exe"),
                    "Actual installed original 1.1.0 client accepts the corrected build's update descriptor");
            }
            finally { context.Unload(); }
        }
        var bytes=Encoding.UTF8.GetBytes("verified test payload, never executed");
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        var json=System.Text.Json.JsonSerializer.Serialize(new {version="1.0.2",installerUrl="https://updates.example/installer.exe",sha256=hash});
        var parsed=UpdateService.Parse(json,"1.0.1");
        check(parsed.UpdateAvailable && parsed.LatestVersion=="1.0.2", "Updater detects a newer version with a verified installer descriptor");
        check(!UpdateService.Parse(json,"1.0.2").UpdateAvailable && !UpdateService.Parse(json,"1.0.3").UpdateAvailable,"Updater skips equal and older versions");
        var rejected=false;
        try { UpdateService.Parse(json.Replace("https://", "http://"),"1.0.1"); } catch(InvalidDataException){rejected=true;}
        check(rejected,"Updater refuses insecure installer URLs");
        using var http=new HttpClient(new PayloadHandler(bytes));
        var service=new UpdateService(http);
        var path=await service.DownloadAsync(parsed);
        check(File.ReadAllBytes(path).SequenceEqual(bytes),"Updater downloads and verifies the entire installer before making it available");
        Directory.Delete(Path.GetDirectoryName(path)!,true);
        rejected=false;
        try { await service.DownloadAsync(parsed with {Sha256=new string('0',64)}); } catch(InvalidDataException){rejected=true;}
        check(rejected,"Updater rejects a corrupted installer instead of running it");
        using var missingHttp = new HttpClient(new PayloadHandler(Array.Empty<byte>(), HttpStatusCode.NotFound));
        var noStable = await new UpdateService(missingHttp).CheckAsync(new AppSettings().UpdateFeedUrl, "1.0.0");
        check(noStable.Configured && !noStable.UpdateAvailable && noStable.Message.Contains("No stable update"), "GitHub without a stable release gives a clear non-error status");
        var noFeed=await service.CheckAsync("","1.0.1");
        check(!noFeed.Configured && !noFeed.UpdateAvailable,"Missing public update hosting is reported clearly");
    }
    private sealed class PayloadHandler(byte[] payload, HttpStatusCode status = HttpStatusCode.OK):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(status){RequestMessage=request,Content=new ByteArrayContent(payload)});
    }
}



