using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AccessibleOCRStudio;
using AccessibleOCRStudio.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message); passed++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-launch-assets")
        {
            MakeLaunchAssets(args[1]);
            PaddleChecks.MakeScreenshots(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--ocr-quality-audit")
        {
            RunQualityAudit(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--prepare-pdf-fixtures")
        {
            Directory.CreateDirectory(args[1]);
            var arabic = Path.Combine(args[1], "arabic.png");
            MakeImage(arabic, "مرحبا بالعالم\nهذا اختبار للتعرف على النص العربي", true);
            new SearchablePdfService().CreateAsync(arabic, Path.Combine(args[1], "arabic.pdf"), new AppSettings { OcrLanguages = "ara", Deskew = false, AutoRotate = false }).GetAwaiter().GetResult();
            var body = Path.Combine(args[1], "body.png");
            MakeImage(body, "Scanned body with quick brown fox.\nREPEATED PHRASE", false);
            MakeHybridPdf(Path.Combine(args[1], "hybrid.pdf"), body);
            MakeHybridPdf(Path.Combine(args[1], "rotated.pdf"), body, rotation: 90);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--prepare-ui-fixtures")
        {
            Directory.CreateDirectory(args[1]);
            MakeImage(Path.Combine(args[1], "english.png"), "TextBridge\nThe quick brown fox jumps over the lazy dog.\nThis is a keyboard and screen reader test.", false);
            MakeImage(Path.Combine(args[1], "french.png"), "Reconnaissance de texte\nUne école française pendant été.\nBonjour le monde.", false);
            MakeImage(Path.Combine(args[1], "arabic.png"), "مرحبا بالعالم\nهذا اختبار للتعرف على النص العربي", true);
            Console.WriteLine("Prepared synthetic UI test fixtures.");
            return 0;
        }
        if (args.Contains("--author-checks-only"))
        {
            AuthorInformationChecks.Run(Check);
            Console.WriteLine($"All {passed} author-information checks passed.");
            return 0;
        }
        var temp = Path.Combine(Path.GetTempPath(), "AOS-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            if (args.Contains("--paddle-checks-only"))
            { PaddleChecks.Run(temp, Check); Console.WriteLine($"All {passed} checks passed."); return 0; }
            if (args.Contains("--windows-checks-only"))
            { WindowsOcrChecks.Run(temp, Check); Console.WriteLine($"All {passed} checks passed."); return 0; }
            if (args.Contains("--accessibility-checks-only"))
            { AccessibilityChecks.Run(temp, Check); Console.WriteLine($"All {passed} checks passed."); return 0; }
            if (!args.Contains("--api-checks-only")) { AccessibilityChecks.Run(temp, Check); RevisionChecks.Run(temp, Check); Run(temp); PaddleChecks.Run(temp, Check); WindowsOcrChecks.Run(temp, Check); }
            OpenAiChecks.Run(temp, Check);
            ProviderChecks.Run(temp, Check);
            Console.WriteLine($"All {passed} checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }

    private static void Run(string temp)
    {
        foreach (var language in new[] { "ar", "en", "fr" })
        {
            foreach (var key in new[] { "welcome", "goodbye", "started", "complete", "failed", "openFirst", "unsaved" })
            {
                var message = ExperienceMessages.Get(key, language);
                Check(!string.IsNullOrWhiteSpace(message), $"{language} {key} experience message is available");
                if (key is "welcome" or "goodbye" or "started" or "complete" or "failed")
                    Check(ToastAnnouncements.IsMilestone(message), $"{language} {key} supports fallback screen-reader announcements");
            }
        }
        var oldReportSettingsPath = Path.Combine(temp, "old-report-settings.json");
        new AppSettings { GitHubRepositoryUrl = string.Empty }.Save(oldReportSettingsPath);
        Check(AppSettings.Load(oldReportSettingsPath).GitHubRepositoryUrl == "https://github.com/ihrammal/textbridge", "Existing empty repository settings migrate to the verified reporting destination");
        var reportSettings = new AppSettings { GitHubRepositoryUrl = string.Empty, OcrLanguages = "ara+eng+fra", ApiProvider = "Private provider value must not appear" };
        var reportTime = DateTimeOffset.Parse("2026-10-17T10:00:00Z");
        var report = ProblemReportService.Create("مشكلة — Français", "Open a sample", "Recognized text", "Recognition failed", false, reportSettings, reportTime);
        Check(report.Contains("مشكلة — Français") && report.Contains("Recognition failed") && report.Contains("2026-10-17T10:00:00"), "Problem report preserves multilingual user text and stable creation time");
        Check(!report.Contains("Windows:") && !report.Contains("OCR profile:") && !report.Contains(reportSettings.ApiProvider), "Problem reports exclude optional diagnostics and private provider configuration by default");
        var detailedReport = ProblemReportService.Create("Issue", "Steps", "Expected", "Actual", true, reportSettings, reportTime);
        Check(detailedReport.Contains("Windows:") && detailedReport.Contains("OCR profile: ara+eng+fra") && !detailedReport.Contains(AppContext.BaseDirectory) && !detailedReport.Contains(reportSettings.ApiProvider), "Opt-in diagnostics provide runtime context without local paths or private provider settings");
        var privateProfileSettings = new AppSettings { OcrLanguages = @"C:\private\document.pdf" };
        var privateProfileReport = ProblemReportService.Create("Issue", "Steps", "Expected", "Actual", true, privateProfileSettings, reportTime);
        Check(!privateProfileReport.Contains(privateProfileSettings.OcrLanguages) && privateProfileReport.Contains("Custom (value omitted)"), "Diagnostics never expose a custom OCR profile value that might contain a private path");
        var emailDraft = ProblemReportService.CreateEmailDraft("Issue & question?", report);
        Check(emailDraft.Scheme == "mailto" && emailDraft.AbsoluteUri.Contains(ProblemReportService.SupportEmail) && Uri.UnescapeDataString(emailDraft.Query).Contains("مشكلة — Français"), "Email draft targets the author and safely preserves report Unicode");
        var githubDraft = ProblemReportService.CreateGitHubDraft("https://github.com/example/clear-read", "Issue & title", report);
        Check(githubDraft?.Host == "github.com" && githubDraft.AbsolutePath == "/example/clear-read/issues/new" && Uri.UnescapeDataString(githubDraft.Query).Contains("مشكلة — Français"), "GitHub reporting builds an encoded issue draft without submitting it");
        Check(ProblemReportService.CreateGitHubDraft("https://example.invalid/repo", "issue", report) is null && ProblemReportService.CreateGitHubDraft("", "issue", report) is null, "GitHub reporting rejects missing repositories and non-GitHub destinations");
        var reportWindow = new ReportProblemWindow(reportSettings);
        var summaryField = (TextBox)reportWindow.FindName("Summary");
        var actualField = (TextBox)reportWindow.FindName("Actual");
        Check(!((Button)reportWindow.FindName("SaveReport")).IsEnabled && ((System.Windows.Controls.CheckBox)reportWindow.FindName("IncludeDiagnostics")).IsChecked == false, "Problem report starts with diagnostics off and requires problem details");
        summaryField.Text = "Screen reader issue"; actualField.Text = "Status was not announced";
        Check(((Button)reportWindow.FindName("SaveReport")).IsEnabled && ((Button)reportWindow.FindName("EmailReport")).IsEnabled && ((TextBox)reportWindow.FindName("ReportPreview")).Text.Contains(actualField.Text), "Problem report enables actions and updates its preview when required details are entered");
        Check(AutomationProperties.GetName(summaryField).Contains("required") && !actualField.AcceptsTab, "Problem-report fields expose required labels and do not trap Tab");
        Check(!((Button)reportWindow.FindName("GitHubReport")).IsEnabled && ((TextBlock)reportWindow.FindName("GitHubAvailability")).Text.Contains("not available"), "GitHub action is unavailable until the project repository exists and is configured");
        summaryField.Clear(); actualField.Clear(); reportWindow.Close();
        var configuredReport = new ReportProblemWindow(new AppSettings());
        ((TextBox)configuredReport.FindName("Summary")).Text = "Synthetic test";
        ((TextBox)configuredReport.FindName("Actual")).Text = "Synthetic behavior";
        Check(((Button)configuredReport.FindName("GitHubReport")).IsEnabled && new AppSettings().GitHubRepositoryUrl == "https://github.com/ihrammal/textbridge", "Configured TextBridge repository enables GitHub reporting");
        ((TextBox)configuredReport.FindName("Summary")).Clear(); ((TextBox)configuredReport.FindName("Actual")).Clear(); configuredReport.Close();
        var main = new MainWindow();
        var editor = (TextBox)main.FindName("DocumentText");
        foreach (var pair in new[] { ("CutMenu", System.Windows.Input.ApplicationCommands.Cut), ("CopyMenu", System.Windows.Input.ApplicationCommands.Copy), ("PasteMenu", System.Windows.Input.ApplicationCommands.Paste), ("SelectAllMenu", System.Windows.Input.ApplicationCommands.SelectAll) })
        {
            var item = (MenuItem)main.FindName(pair.Item1);
            Check(item.Command == pair.Item2 && ReferenceEquals(item.CommandTarget, editor), $"{pair.Item1} edits the recognized-document editor");
        }
        editor.Text = "English — Français — العربية";
        System.Windows.Input.ApplicationCommands.SelectAll.Execute(null, editor);
        Check(editor.SelectedText == editor.Text, "Select All selects the complete multilingual recognized text");
        editor.Clear();
        Check(WelcomeService.StartupDuration.TotalSeconds is >= 2 and <= 3, "Welcome screen duration stays between two and three seconds");
        foreach (var language in new[] { "ar", "en", "fr" })
        {
            var welcome = new WelcomeWindow(language);
            Check(welcome.Content is TextBlock label && label.Text == ExperienceMessages.Get("welcome", language) && AutomationProperties.GetName(label) == label.Text,
                $"{language} welcome screen has visible accessible welcome text");
            Check(welcome.Language.IetfLanguageTag == language && (welcome.FlowDirection == FlowDirection.RightToLeft) == (language == "ar"), $"{language} welcome screen supplies pronunciation and reading direction");
            welcome.Close();
        }
        Check(main.Title == "TextBridge", "Main window displays the new TextBridge name");
        var unsaved = typeof(MainWindow).GetProperty("HasUnsavedText", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Check(!(bool)unsaved.GetValue(main)!, "Empty editor has no unsaved text");
        editor.Text = "Recognized text with corrections";
        Check((bool)unsaved.GetValue(main)!, "Recognized or edited text requires saving before replacement or exit");
        typeof(MainWindow).GetField("_savedText", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, editor.Text);
        editor.AppendText(" more edits");
        Check((bool)unsaved.GetValue(main)!, "Further edits after saving are detected");
        editor.Text = "Recognized text with corrections";
        Check(!(bool)unsaved.GetValue(main)!, "Undoing changes to the saved text clears the unsaved state");
        editor.Clear();
        Check(AutomationProperties.GetName(editor) == "Recognized document text", "Main window loads and editor has an accessible name");
        Check(!editor.AcceptsTab, "Tab can leave the document editor without a keyboard trap");
        var status = (TextBlock)main.FindName("StatusText");
        var statusPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status)!;
        status.Text = "Recognition complete.";
        Check(statusPeer.GetName() == "Recognition complete.", "Screen readers can query the current status message on demand");
        status.Text = "Saved document.txt.";
        Check(statusPeer.GetName() == "Saved document.txt.", "Accessible status name follows subsequent operations");
        var notificationRaised = false;
        try { statusPeer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, "Recognition complete.", "test"); notificationRaised = true; }
        catch { }
        Check(notificationRaised, "Status element supports UIA notification announcements for milestones");
        Check(DialogGuard.Absorb(MessageBoxButton.OK) == MessageBoxResult.OK && DialogGuard.Absorb(MessageBoxButton.YesNo) == MessageBoxResult.No && DialogGuard.Absorb(MessageBoxButton.YesNoCancel) == MessageBoxResult.Cancel, "Reentrant dialogs are absorbed with non-destructive answers");
        var preferences = new SettingsWindow(new AppSettings());
        var privacy = new ApiSettingsWindow(new AppSettings());
        ((PasswordBox)privacy.FindName("ApiKey")).Password = "unsaved-test-key";
        ((ComboBox)privacy.FindName("Provider")).SelectedIndex = 2;
        Check(((PasswordBox)privacy.FindName("ApiKey")).Password != "unsaved-test-key", "Switching API providers does not carry credentials across providers");
        var mainSettings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        mainSettings.AnnounceOcrProgress = false;
        typeof(MainWindow).GetMethod("SyncAccessibilityMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
        Check(AutomationProperties.GetLiveSetting((TextBlock)main.FindName("StatusText")) == AutomationLiveSetting.Off, "Progress announcement preference disables the status live region");
        mainSettings.AnnounceOcrProgress = true;
        typeof(MainWindow).GetMethod("SyncAccessibilityMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
        Check(AutomationProperties.GetLiveSetting((TextBlock)main.FindName("StatusText")) == AutomationLiveSetting.Polite, "Progress announcement preference restores the status live region");
        mainSettings.AccessibleReadingMode = false;
        typeof(MainWindow).GetMethod("SyncAccessibilityMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
        var ordinarySize = editor.FontSize;
        mainSettings.AccessibleReadingMode = true;
        typeof(MainWindow).GetMethod("SyncAccessibilityMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
        Check(editor.FontSize >= 18 && editor.FontSize >= ordinarySize && editor.Padding.Left == 8,
            "Accessible reading mode enlarges document text and adds reading space");
        Check(main.MinWidth <= 360 && main.MinHeight <= 320, "Main window can fit smaller display work areas");
        var menu = (Menu)main.FindName("ApplicationMenu");
        menu.Measure(new Size(360, double.PositiveInfinity));
        Check(menu.DesiredSize.Width <= 360 && menu.DesiredSize.Height > 0, "Main menu fits a narrow window without horizontal clipping");
        var find = new FindReplaceWindow(editor, true);
        Check(find.ResizeMode == ResizeMode.CanResize && find.Content is ScrollViewer,
            "Find/Replace can resize and scroll rather than hiding controls");
        editor.Text = "one ONE one";
        ((TextBox)find.FindName("FindText")).Text = "one";
        ((TextBox)find.FindName("ReplaceText")).Text = "two";
        typeof(FindReplaceWindow).GetMethod("ReplaceAll_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(find, new object[] { find, new RoutedEventArgs() });
        Check(editor.Text == "two two two", "Replace All handles repeated and case-insensitive matches");
        editor.Text = "one";
        editor.Select(0, 3);
        typeof(FindReplaceWindow).GetMethod("Replace_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(find, new object[] { find, new RoutedEventArgs() });
        Check(editor.Text == "two" && ((TextBlock)find.FindName("ResultText")).Text == "One occurrence replaced. No further matches.", "Replacing the last match announces success instead of failure");
        var findOnly = new FindReplaceWindow(editor, false);
        Check(!((Button)findOnly.FindName("ReplaceButton")).IsEnabled && !((Button)findOnly.FindName("ReplaceAllButton")).IsEnabled, "Find-only dialog cannot replace or erase document content");
        CaptureLayout(main, "main", 960, 500);
        CaptureLayout(find, "find-replace", 520, 270);
        CaptureLayout(preferences, "preferences", 720, 500);
        CaptureLayout(privacy, "privacy", 700, 500);
        _ = new SettingsWindow(new AppSettings());
        Check(true, "Preferences XAML loads");

        var export = new ExportService();
        var text = "CHAPTER ONE\nThis is a sentence.\nمرحبا بالعالم\nFrançais : été, école.\n- First item\n- Second item";
        var txt = Path.Combine(temp, "document.txt");
        export.SaveTextAsync(txt, text).GetAwaiter().GetResult();
        Check(File.ReadAllText(txt) == text, "TXT preserves Arabic, French accents and line breaks");
        var docx = Path.Combine(temp, "document.docx");
        export.SaveAccessibleDocx(docx, text);
        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var errors = new OpenXmlValidator().Validate(doc).ToList();
            Check(errors.Count == 0, "DOCX passes Open XML schema validation: " + string.Join("; ", errors.Select(x => x.Description)));
            var body = doc.MainDocumentPart!.Document!;
            Check(body.Descendants<BiDi>().Any(), "DOCX contains RTL paragraph properties");
            Check(body.Descendants<ParagraphStyleId>().Any(x => x.Val == "Heading1"), "DOCX contains semantic heading styles");
            Check(body.Descendants<Languages>().Any(x => x.Val == "ar-SA"), "DOCX marks Arabic runs with an Arabic language attribute");
            Check(body.Descendants<Languages>().Any(x => x.Val == "fr-FR"), "DOCX marks French runs with a French language attribute");
        }
        var html = Path.Combine(temp, "document.html");
        export.SaveHtmlAsync(html, text + "\n<script>alert(1)</script>").GetAwaiter().GetResult();
        var markup = File.ReadAllText(html);
        Check(markup.Contains("dir=\"rtl\"") && markup.Contains("&lt;script&gt;"), "HTML preserves RTL and escapes document content");
        Check(markup.Contains("<ul>") && markup.Contains("<li dir=\"auto\" lang=\"en\">First item</li>") && !markup.Contains("role=\"listitem\""), "HTML exposes lists with semantic list containers");
        Check(markup.Contains("lang=\"ar\"") && markup.Contains("lang=\"fr\"") && markup.Contains("lang=\"en\""), "HTML marks Arabic, French and English content with language attributes");
        var arabicHtml = Path.Combine(temp, "arabic-document.html");
        export.SaveHtmlAsync(arabicHtml, "مرحبا بالعالم\nهذا اختبار للتعرف على النص العربي").GetAwaiter().GetResult();
        var arabicMarkup = File.ReadAllText(arabicHtml);
        Check(arabicMarkup.Contains("<html lang=\"ar\">") && arabicMarkup.Contains("dir=\"rtl\" lang=\"ar\""), "HTML export labels an Arabic document as Arabic");
        var md = Path.Combine(temp, "document.md");
        export.SaveMarkdownAsync(md, text).GetAwaiter().GetResult();
        Check(File.ReadAllText(md).Contains(text), "Markdown preserves Unicode");
        Check(new TableAccessibilityService().Linearize("Name\tAmount\nAlice\t10").Contains("Amount: 10"), "Table linearization associates values with column headers");
        var pageNumber = typeof(SearchablePdfService).GetMethod("PageNumber", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sorted = new[] { "page-10.png", "page-2.png", "page-1.png" }.OrderBy(x => (int)pageNumber.Invoke(null, new object[] { x })!).ToArray();
        Check(sorted.SequenceEqual(new[] { "page-1.png", "page-2.png", "page-10.png" }), "PDF pages retain numeric reading order beyond page nine");
        Check(new RuntimeDiagnosticsService().Check().CoreOcrReady, "Bundled OCR engine and language data are present");

        var ocr = new LocalOcrService();
        var samples = new[] {
            (Language: "eng", Text: "Accessible OCR Studio\nThis document contains English printed text.\nThe quick brown fox jumps over the lazy dog.", Expected: "quick brown fox"),
            (Language: "fra", Text: "Reconnaissance de texte\nUne école française pendant été.\nBonjour le monde.", Expected: "école"),
            (Language: "ara", Text: "مرحبا بالعالم\nهذا اختبار للتعرف على النص العربي", Expected: "مرحبا")
        };
        var images = new List<string>();
        foreach (var sample in samples)
        {
            var image = Path.Combine(temp, sample.Language + ".png");
            MakeImage(image, sample.Text, sample.Language == "ara"); images.Add(image);
            var settings = new AppSettings { OcrLanguages = sample.Language, Deskew = false, AutoRotate = false };
            var result = ocr.RecognizeWithConfidenceAsync(image, settings).GetAwaiter().GetResult();
            Check(result.PlainText.Contains(sample.Expected, StringComparison.OrdinalIgnoreCase), sample.Language + " image OCR: " + result.PlainText.Trim().Replace('\n', ' '));
            Check(result.Words.Count > 0, sample.Language + " word confidence data is available");
            var languagePdf = Path.Combine(temp, sample.Language + "-searchable.pdf");
            new SearchablePdfService().CreateAsync(image, languagePdf, settings).GetAwaiter().GetResult();
            var languageText = ocr.RecognizeAsync(languagePdf, settings).GetAwaiter().GetResult();
            Check(languageText.Split(sample.Expected, StringSplitOptions.None).Length == 2, sample.Language + " searchable PDF preserves native Unicode text without duplication: " + languageText);
        }
        var arabicRows = Path.Combine(temp, "arabic-connected-rows.png");
        const string arabicRowsText = "القراءة حق للجميع\nأحمد يقرأ كتابا في المكتبة\nإلى أين تذهب الأسرة اليوم؟";
        MakeImage(arabicRows, arabicRowsText, true);
        foreach (var profile in new[] { "ara", "ara+eng+fra" })
        {
            var recognizedRows = ocr.RecognizeAsync(arabicRows, new AppSettings { OcrLanguages = profile, Deskew = false, AutoRotate = false }).GetAwaiter().GetResult();
            Check(arabicRowsText.Split('\n').All(line => recognizedRows.Contains(line)), profile + " preserves all Arabic rows including connected letter shapes");
        }
        var mixed = Path.Combine(temp, "mixed.png");
        MakeImage(mixed, "Accessible OCR Studio\nBonjour le monde\nمرحبا بالعالم", false);
        var mixedText = ocr.RecognizeAsync(mixed, new AppSettings { Deskew = false, AutoRotate = false }).GetAwaiter().GetResult();
        Check(mixedText.Contains("Accessible") && mixedText.Contains("Bonjour") && mixedText.Contains("مرحبا"), "Mixed Arabic, English and French OCR");
        var pdf = Path.Combine(temp, "searchable.pdf");
        var english = new AppSettings { OcrLanguages = "eng", Deskew = false, AutoRotate = false };
        new SearchablePdfService().CreateAsync(images[0], pdf, english).GetAwaiter().GetResult();
        var progress = new List<string>();
        var digital = ocr.RecognizeAsync(pdf, english, new ImmediateProgress(progress)).GetAwaiter().GetResult();
        Check(digital.Contains("quick brown fox") && progress.Any(x => x.Contains("Digital PDF")), "Searchable PDF has selectable text and digital-PDF extraction works");
        english.PreferDigitalPdfText = false;
        Check(ocr.RecognizeAsync(pdf, english).GetAwaiter().GetResult().Contains("quick brown fox"), "PDF rendering and scanned-page OCR work");
        var batch = Path.Combine(temp, "batch");
        english.PreferDigitalPdfText = true;
        var count = new BatchService().ConvertToTextAsync(new[] { images[0], pdf }, batch, english).GetAwaiter().GetResult();
        Check(count == 2 && Directory.GetFiles(batch, "*.txt").Length == 2, "Batch converts image and PDF inputs");
        var multiple = Path.Combine(temp, "multiple.pdf");
        var mergeInfo = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pdf", "pdfunite.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { pdf, pdf, pdf, multiple }) mergeInfo.ArgumentList.Add(argument);
        using (var merge = System.Diagnostics.Process.Start(mergeInfo)!) { merge.WaitForExit(); Check(merge.ExitCode == 0, "Multi-page PDF fixture was created"); }
        var multipleOutput = Path.Combine(temp, "multiple-searchable.pdf");
        new SearchablePdfService().CreateAsync(multiple, multipleOutput, english).GetAwaiter().GetResult();
        var multipleText = ocr.RecognizeAsync(multipleOutput, english).GetAwaiter().GetResult();
        Check(multipleText.Split("quick brown fox").Length == 4, "Multi-page searchable PDF export preserves every page's text");
        RunExtended(temp, ocr, english, images[0], pdf);
    }

    private static void RunExtended(string temp, LocalOcrService ocr, AppSettings english, string image, string digitalPdf)
    {
        var magick = Path.Combine(AppContext.BaseDirectory, "image", "magick.exe");
        foreach (var fixture in new[] { (Name: "rotated", Arguments: new[] { "-rotate", "90" }), (Name: "skewed", Arguments: new[] { "-rotate", "5" }), (Name: "low-contrast", Arguments: new[] { "-level", "-200%,300%" }) })
        {
            var altered = Path.Combine(temp, fixture.Name + ".png");
            RunTool(magick, new[] { image }.Concat(fixture.Arguments).Concat(new[] { altered }).ToArray());
            var settings = new AppSettings { OcrLanguages = "eng", Deskew = true, Grayscale = true, AutoRotate = true };
            var result = ocr.RecognizeAsync(altered, settings).GetAwaiter().GetResult();
            Check(result.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase), fixture.Name + " scan OCR preserves expected text");
        }
        var prepared = new ImagePreprocessingService().PrepareAsync(image, new AppSettings { Grayscale = true, Deskew = true }).GetAwaiter().GetResult();
        Check(prepared.Temporary && File.Exists(prepared.Path), "Grayscale and deskew create a temporary preprocessed image");
        File.Delete(prepared.Path);
        var scannedPdf = Path.Combine(temp, "image-only.pdf");
        RunTool(magick, image, "-density", "150", scannedPdf);
        var nativePdf = Path.Combine(temp, "native-only.pdf");
        MakeHybridPdf(nativePdf, null);
        var mixedPdf = Path.Combine(temp, "mixed-pages.pdf");
        RunTool(Path.Combine(AppContext.BaseDirectory, "pdf", "pdfunite.exe"), scannedPdf, nativePdf, mixedPdf);
        var mixedProgress = new List<string>();
        var mixed = ocr.RecognizeAsync(mixedPdf, english, new ImmediateProgress(mixedProgress)).GetAwaiter().GetResult();
        Check(mixed.Contains("quick brown fox") && mixed.Contains("ExactNative123"), "Mixed scanned/digital PDF preserves text from both pages");
        Check(mixedProgress.Any(x => x.Contains("Recognizing page 1")) && mixedProgress.Any(x => x.Contains("digital PDF text for page 2")), "Mixed PDF uses OCR only for the scanned page");
        RunHybridPdfChecks(temp, ocr, english, nativePdf, digitalPdf, scannedPdf);

        var collisions = Path.Combine(temp, "collision-batch");
        Directory.CreateDirectory(collisions);
        File.WriteAllText(Path.Combine(collisions, "eng.txt"), "Existing output must survive.");
        new BatchService().ConvertToTextAsync(new[] { image, image }, collisions, english).GetAwaiter().GetResult();
        Check(File.ReadAllText(Path.Combine(collisions, "eng.txt")) == "Existing output must survive." && Directory.GetFiles(collisions, "*.txt").Length == 3, "Batch preserves existing outputs and handles duplicate source names");

        var settingsPath = Path.Combine(temp, "settings.json");
        var saved = new AppSettings { MessageLanguage = "ar", OcrLanguages = "fra", LowConfidenceThreshold = 55, PlayStartupSound = false };
        saved.Save(settingsPath);
        var loaded = AppSettings.Load(settingsPath);
        Check(loaded.MessageLanguage == "ar" && loaded.OcrLanguages == "fra" && loaded.LowConfidenceThreshold == 55 && !loaded.PlayStartupSound, "Settings round-trip preserves user preferences");
        File.WriteAllText(settingsPath, "{ invalid json");
        Check(AppSettings.Load(settingsPath).OfflineOnlyMode, "Corrupt settings recover to privacy-preserving defaults");
        const string key = "fake-test-key-never-transmitted";
        var legacyRoot = Path.Combine(temp, "legacy-appdata");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "Accessible OCR Studio"));
        File.WriteAllText(Path.Combine(legacyRoot, "Accessible OCR Studio", "settings.json"), "{\"OcrLanguages\":\"fra\"}");
        AppSettings.MigrateFromLegacy(Path.Combine(legacyRoot, "Accessible OCR Studio"), Path.Combine(legacyRoot, "TextBridge"), "settings.json");
        Check(AppSettings.Load(Path.Combine(legacyRoot, "TextBridge", "settings.json")).OcrLanguages == "fra", "Legacy Accessible OCR Studio settings migrate into the TextBridge folder");
        var legacyCredentials = Path.Combine(temp, "legacy-credentials");
        var legacyStore = new CredentialService(legacyCredentials);
        legacyStore.Save("TestProvider", key);
        Check(legacyStore.Load("TestProvider") == key, "Legacy credential store writes protected keys");
        var migratedCredentials = Path.Combine(temp, "migrated-credentials");
        CredentialService.MigrateFromLegacy(legacyCredentials, migratedCredentials);
        Check(new CredentialService(migratedCredentials).Load("TestProvider") == key, "Legacy credentials migrate into the TextBridge folder");
        var credentialFolder = Path.Combine(temp, "credentials");
        var credentials = new CredentialService(credentialFolder);
        credentials.Save("TestProvider", key);
        Check(credentials.Load("TestProvider") == key, "Windows-protected credentials round-trip");
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(credentialFolder, "testprovider.bin"))).Contains(key), "Stored credentials do not expose plaintext keys");
        saved.Save(settingsPath);
        Check(!File.ReadAllText(settingsPath).Contains(key), "Credentials remain outside settings.json");
        File.WriteAllBytes(Path.Combine(credentialFolder, "testprovider.bin"), new byte[] { 1, 2, 3 });
        Check(credentials.Load("TestProvider") is null, "Corrupt protected credentials fail safely");
        credentials.Delete("TestProvider");
        Check(!File.Exists(Path.Combine(credentialFolder, "testprovider.bin")), "Cleared credentials are removed");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "AccessibleOCRStudio");
        var before = Directory.Exists(temporaryRoot) ? Directory.GetFileSystemEntries(temporaryRoot).ToHashSet() : new HashSet<string>();
        var brokenPdf = Path.Combine(temp, "broken.pdf");
        File.WriteAllText(brokenPdf, "This is not a PDF.");
        var failed = false;
        try { ocr.RecognizeAsync(brokenPdf, english).GetAwaiter().GetResult(); }
        catch (InvalidOperationException ex) { failed = ex.Message.Contains("Unable to render the PDF"); }
        Check(failed, "Malformed PDFs produce a clear rendering error");
        Check(before.SetEquals(Directory.GetFileSystemEntries(temporaryRoot)), "Failed PDF recognition cleans up its temporary files");
        var existingPdf = Path.Combine(temp, "preserve.pdf");
        File.WriteAllText(existingPdf, "Existing output");
        failed = false;
        try { new SearchablePdfService().CreateAsync(brokenPdf, existingPdf, english).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && File.ReadAllText(existingPdf) == "Existing output", "Failed searchable PDF creation preserves an existing output");
        Check(before.SetEquals(Directory.GetFileSystemEntries(temporaryRoot)), "Failed searchable PDF creation cleans up temporary files");
        failed = false;
        try { ocr.RecognizeAsync(image, new AppSettings { OcrLanguages = "aos_missing_language", Deskew = false, AutoRotate = false }).GetAwaiter().GetResult(); }
        catch (InvalidOperationException ex) { failed = ex.Message.Contains("aos_missing_language"); }
        Check(failed, "Missing language data produces an actionable OCR error");
    }

    private static void RunTool(string executable, params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }

    private static void MakeLaunchAssets(string output)
    {
        Directory.CreateDirectory(output);
        foreach (var size in new[] { (W: 1080, H: 1080, Name: "square"), (W: 1080, H: 1350, Name: "portrait"), (W: 1080, H: 1920, Name: "story"), (W: 1200, H: 630, Name: "landscape") })
        foreach (var arabic in new[] { false, true })
        {
            var visual = new DrawingVisual();
            using (var d = visual.RenderOpen())
            {
                var navy = new SolidColorBrush(System.Windows.Media.Color.FromRgb(12,35,64));
                var mint = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140,229,209));
                var light = new SolidColorBrush(System.Windows.Media.Color.FromRgb(247,250,253));
                d.DrawRectangle(navy, null, new Rect(0,0,size.W,size.H));
                d.DrawRectangle(mint, null, new Rect(0,0,18,size.H));
                var shortCard = size.H == 630;
                void Text(string text, double y, double font, Brush brush, bool rtl = false, bool bold = false)
                {
                    var f = new FormattedText(text, CultureInfo.GetCultureInfo(rtl ? "ar" : "en"), rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), font, brush, 1);
                    d.DrawText(f,new Point(rtl ? size.W-80 : 80,y));
                }
                Text("TextBridge 1.1", shortCard ? 42 : 70, shortCard ? 60 : 76,light,false,true);
                Text(arabic ? "اقرأ. عدّل. احفظ." : "Read. Edit. Save.",shortCard ? 140 : size.H == 1920 ? 300 : 230,shortCard ? 64 : 76,mint,arabic,true);
                var start = shortCard ? 245 : size.H == 1920 ? 440 : 375;
                Text(arabic ? "حوّل الصور والمستندات الممسوحة" : "Turn images and scanned documents",start,shortCard ? 32 : 38,light,arabic);
                Text(arabic ? "إلى نص يمكنك قراءته وتعديله." : "into text you can read and edit.",start+60,shortCard ? 32 : 38,light,arabic);
                Text(arabic ? "العربية • الإنجليزية • الفرنسية" : "Arabic • English • French",start+145,shortCard ? 30 : 34,mint,arabic,true);
                if (!shortCard) Text(arabic ? "اختر PaddleOCR أو محرك Windows" : "Choose PaddleOCR or Windows OCR",start+220,30,light,arabic);
                if (!shortCard)
                {
                    var boxY = size.H == 1920 ? 1070 : size.H == 1350 ? 800 : 680;
                    d.DrawRoundedRectangle(light,null,new Rect(70,boxY,size.W-140,150),18,18);
                    Text("Ctrl+A    Ctrl+X    Ctrl+C    Ctrl+V",boxY+25,36,navy,false,true);
                    Text(arabic ? "تحديد الكل • قص • نسخ • لصق" : "Select all • Cut • Copy • Paste",boxY+85,29,navy,arabic);
                    Text(arabic ? "تحقّق من النص، وخصوصاً الأرقام." : "Review recognized text, especially numbers.",size.H-185,27,light,arabic);
                }
                Text("github.com/ihrammal/textbridge",size.H-112,shortCard ? 27 : 30,light);
                Text(arabic ? "إعداد إيمان رمال" : "By Iman Rammal",size.H-65,24,mint,arabic);
            }
            var bitmap = new RenderTargetBitmap(size.W,size.H,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output,$"textbridge-{(arabic ? "ar" : "en")}-{size.Name}.png"));encoder.Save(stream);
        }
    }

    private static void RunQualityAudit(string output)
    {
        Directory.CreateDirectory(output);
        var samples = new[] {
            (Id: "arabic-basic", Language: "ara", Text: "مرحبا بالعالم\nهذا اختبار للتعرف على النص العربي\nيمكنك قراءة النص وتعديله وحفظه"),
            (Id: "arabic-hamza", Language: "ara", Text: "القراءة حق للجميع\nأحمد يقرأ كتابا في المكتبة\nإلى أين تذهب الأسرة اليوم؟"),
            (Id: "arabic-numbers", Language: "ara", Text: "تقرير المدرسة\nعدد الطلاب ١٢٥ طالبا\nموعد الاجتماع يوم الأحد"),
            (Id: "french-accents", Language: "fra", Text: "Une école française\nLes élèves étudient à la bibliothèque.\nNoël, été, forêt et cœur."),
            (Id: "french-apostrophes", Language: "fra", Text: "L'accès à la lecture pour tous\nAujourd'hui, je peux lire et modifier le texte.\nC'est simple et pratique !"),
            (Id: "french-numbers", Language: "fra", Text: "Compte rendu de réunion\nLe rendez-vous est prévu à 14 h 30.\nNous avons reçu 125 documents.")
        };
        var results = new List<object>();
        var ocr = new LocalOcrService();
        foreach (var sample in samples)
        {
            var image = Path.Combine(output, sample.Id + ".png");
            MakeImage(image, sample.Text, sample.Language == "ara");
            File.WriteAllText(Path.Combine(output, sample.Id + "-reference.txt"), sample.Text);
            foreach (var profile in new[] { sample.Language, "ara+eng+fra" })
            foreach (var layoutMode in new[] { "accurate", "fast", "automatic" })
            {
                var actual = ocr.RecognizeAsync(image, new AppSettings { OcrLanguages = profile, OcrMode = layoutMode == "automatic" ? "accurate" : layoutMode, Deskew = false, AutoRotate = layoutMode == "automatic" }).GetAwaiter().GetResult().Trim();
                var expectedNormalized = System.Text.RegularExpressions.Regex.Replace(sample.Text.Normalize(), @"\s+", " ").Trim();
                var actualNormalized = System.Text.RegularExpressions.Regex.Replace(actual.Normalize(), @"\s+", " ").Trim();
                var edits = EditDistance(expectedNormalized.ToCharArray(), actualNormalized.ToCharArray());
                var words = expectedNormalized.Split(' ');
                var wordEdits = EditDistance(words, actualNormalized.Split(' '));
                var cer = (double)edits / expectedNormalized.Length;
                var wer = (double)wordEdits / words.Length;
                File.WriteAllText(Path.Combine(output, sample.Id + "-" + layoutMode + "-" + profile.Replace('+', '-') + ".txt"), actual);
                results.Add(new { sample.Id, sample.Language, Profile = profile, LayoutMode = layoutMode, Reference = sample.Text, Actual = actual, CharacterEdits = edits, ReferenceCharacters = expectedNormalized.Length, CharacterErrorRate = cer, WordErrorRate = wer });
                Console.WriteLine($"{sample.Id} [{profile}/{layoutMode}]: CER {cer:P2}, WER {wer:P2}");
            }
        }
        File.WriteAllText(Path.Combine(output, "results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private static int EditDistance<T>(T[] reference, T[] actual)
    {
        var previous = Enumerable.Range(0, actual.Length + 1).ToArray();
        for (var i = 1; i <= reference.Length; i++)
        {
            var current = new int[actual.Length + 1]; current[0] = i;
            for (var j = 1; j <= actual.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (EqualityComparer<T>.Default.Equals(reference[i - 1], actual[j - 1]) ? 0 : 1));
            previous = current;
        }
        return previous[actual.Length];
    }

    private static void MakeImage(string path, string text, bool rtl)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1800, 600));
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo(rtl ? "ar" : "en"), rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, new Typeface("Arial"), 48, Brushes.Black, 1);
            drawing.DrawText(formatted, new Point(rtl ? 1700 : 60, 60));
        }
        var bitmap = new RenderTargetBitmap(1800, 600, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void RunHybridPdfChecks(string temp, LocalOcrService ocr, AppSettings settings, string nativePdf, string searchablePdf, string scannedPdf)
    {
        var progress = new List<string>();
        var native = ocr.RecognizeAsync(nativePdf, settings, new ImmediateProgress(progress)).GetAwaiter().GetResult();
        Check(native.Contains("ExactNative123") && progress.Any(x => x.Contains("OCR was not required")), "Image-free digital PDF preserves exact text without OCR");

        var image = Path.Combine(temp, "hybrid-body.png");
        MakeImage(image, "Scanned body with quick brown fox.\nREPEATED PHRASE", false);
        var hybridPdf = Path.Combine(temp, "hybrid-page.pdf");
        MakeHybridPdf(hybridPdf, image);
        progress.Clear();
        var hybrid = ocr.RecognizeAsync(hybridPdf, settings, new ImmediateProgress(progress)).GetAwaiter().GetResult();
        Check(hybrid.Contains("ExactNative123") && hybrid.Contains("quick brown fox"), "One PDF page preserves both native heading and scanned body");
        Check(hybrid.Split("ExactNative123").Length == 2, "Native PDF text is not duplicated by raster OCR");
        Check(hybrid.Split("REPEATED PHRASE").Length == 3, "Identical text in separate native and raster positions is retained twice");
        Check(hybrid.IndexOf("ExactNative123", StringComparison.Ordinal) < hybrid.IndexOf("quick brown fox", StringComparison.Ordinal) &&
            hybrid.IndexOf("quick brown fox", StringComparison.Ordinal) < hybrid.LastIndexOf("REPEATED PHRASE", StringComparison.Ordinal), "Hybrid page keeps heading, scanned body and footer in vertical reading order");
        Check(progress.Any(x => x.Contains("Recognizing scanned content")), "Hybrid-page progress explains why OCR is needed");

        var searchable = ocr.RecognizeAsync(searchablePdf, settings).GetAwaiter().GetResult();
        Check(searchable.Split("quick brown fox").Length == 2, "Existing searchable PDF text layer is not duplicated");

        var croppedPdf = Path.Combine(temp, "hybrid-cropped.pdf");
        MakeHybridPdf(croppedPdf, image, cropped: true);
        var cropped = ocr.RecognizeAsync(croppedPdf, settings).GetAwaiter().GetResult();
        Check(cropped.Contains("quick brown fox") && cropped.Split("ExactNative123").Length == 2, "Nonzero PDF crop box keeps native and OCR coordinates aligned");

        foreach (var rotation in new[] { 90, 180, 270 })
        {
            var rotatedPdf = Path.Combine(temp, $"hybrid-rotated-{rotation}.pdf");
            MakeHybridPdf(rotatedPdf, image, rotation: rotation);
            var rotated = ocr.RecognizeAsync(rotatedPdf, new AppSettings { OcrLanguages = "eng", Deskew = false, AutoRotate = true }).GetAwaiter().GetResult();
            Check(rotated.Contains("quick brown fox") && rotated.Split("ExactNative123").Length == 2 &&
                rotated.IndexOf("ExactNative123", StringComparison.Ordinal) < rotated.IndexOf("quick brown fox", StringComparison.Ordinal) &&
                rotated.IndexOf("quick brown fox", StringComparison.Ordinal) < rotated.LastIndexOf("REPEATED PHRASE", StringComparison.Ordinal),
                $"{rotation}-degree hybrid PDF preserves scanned content, native text and reading order: " + rotated);
        }

        var mixed = Path.Combine(temp, "hybrid-multipage.pdf");
        RunTool(Path.Combine(AppContext.BaseDirectory, "pdf", "pdfunite.exe"), hybridPdf, nativePdf, scannedPdf, mixed);
        progress.Clear();
        var text = ocr.RecognizeAsync(mixed, settings, new ImmediateProgress(progress)).GetAwaiter().GetResult();
        Check(text.Split("ExactNative123").Length == 3 && text.Split("quick brown fox").Length == 3 && text.Contains("--- Page 3 ---"), "Hybrid, native and scanned pages all survive in one document");
        Check(progress.Any(x => x.Contains("digital PDF text for page 2")) && progress.Any(x => x.Contains("Recognizing page 3")), "Mixed document chooses recognition separately for each page");
    }

    private static void MakeHybridPdf(string path, string? image, bool cropped = false, int rotation = 0)
    {
        static byte[] Ascii(string value) => System.Text.Encoding.ASCII.GetBytes(value);
        static byte[] StreamObject(string dictionary, byte[] data)
        {
            using var buffer = new MemoryStream();
            buffer.Write(Ascii($"<< {dictionary} /Length {data.Length} >>\nstream\n"));
            buffer.Write(data); buffer.Write(Ascii("\nendstream"));
            return buffer.ToArray();
        }
        var content = "BT /F1 24 Tf 72 720 Td (ExactNative123 heading) Tj ET\n" +
            (image is null ? "" : "q 540 0 0 180 36 420 cm /Im0 Do Q\n") +
            "BT /F1 20 Tf 72 350 Td (REPEATED PHRASE) Tj ET\n";
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                $"/Rotate {rotation} " +
                (cropped ? "/CropBox [24 24 588 768] " : "") +
                "/Resources << /Font << /F1 4 0 R >> " + (image is null ? "" : "/XObject << /Im0 6 0 R >> ") + ">> /Contents 5 0 R >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
            StreamObject("", Ascii(content))
        };
        if (image is not null)
        {
            using var source = File.OpenRead(image);
            var frame = BitmapDecoder.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var jpeg = new MemoryStream(); encoder.Save(jpeg);
            objects.Add(StreamObject($"/Type /XObject /Subtype /Image /Width {frame.PixelWidth} /Height {frame.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", jpeg.ToArray()));
        }
        using var pdf = File.Create(path);
        pdf.Write(Ascii("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            pdf.Write(Ascii($"{i + 1} 0 obj\n")); pdf.Write(objects[i]); pdf.Write(Ascii("\nendobj\n"));
        }
        var xref = pdf.Position;
        pdf.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) pdf.Write(Ascii($"{offset:D10} 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
    }

    private static void CaptureLayout(Window window, string name, int width, int height)
    {
        var output = Environment.GetEnvironmentVariable("AOS_LAYOUT_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }

    private sealed class ImmediateProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }
}






