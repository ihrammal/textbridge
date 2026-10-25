using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;

public partial class MainWindow : Window
{
    private static string AppVersion => typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private readonly WelcomeService _welcome = new();
    private readonly LocalOcrService _ocr = new();
    private readonly ExportService _export = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly RecognitionReviewService _review = new();
    private readonly UpdateService _updates = new();
    private readonly TableAccessibilityService _tables = new();
    private readonly RuntimeDiagnosticsService _diagnostics = new();
    private readonly SearchablePdfService _searchablePdf = new();
    private RecognitionDocument? _lastRecognition;
    private int _lowConfidenceIndex = -1;
    private string? _currentFile;
    private string? _lastTextSavePath;
    private bool _allowClose;
    private bool _altPressed;
    private bool _altUsedWithOtherKey;
    private UIElement? _menuReturnFocus;
    private CancellationTokenSource? _apiCancellation;
    private bool _busy;
    private bool _updating;
    private bool _closing;
    private string _savedText = string.Empty;
    private bool HasUnsavedText => DocumentText.Text != _savedText;

    public MainWindow()
    {
        InitializeComponent(); LiveRegionAnnouncements.Attach(StatusText, DocumentText);
        foreach (var item in new[] { UndoMenu, RedoMenu, CutMenu, CopyMenu, PasteMenu, SelectAllMenu }) item.CommandTarget = DocumentText;
        DocumentText.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyOrCut_Executed, ClipboardCommand_CanExecute));
        DocumentText.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, CopyOrCut_Executed, ClipboardCommand_CanExecute));
        Deactivated += (_, _) => { _altPressed = false; _altUsedWithOtherKey = false; };
        Loaded += (_, _) => SystemParameters.StaticPropertyChanged += SystemAppearanceChanged;
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= SystemAppearanceChanged;
        SyncAccessibilityMenu();
    }

    private void SystemAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.Invoke(SyncAccessibilityMenu);
    }

    private void ClipboardCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    { e.CanExecute = true; e.Handled = true; }

    private void CopyOrCut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (DocumentText.SelectionLength == 0)
        {
            StatusText.Text = UiLanguage.T("Select text before using Cut or Copy.");
            DocumentText.Focus();
            return;
        }
        try
        {
            Clipboard.SetText(DocumentText.SelectedText, TextDataFormat.UnicodeText);
            if (e.Command == ApplicationCommands.Cut) DocumentText.SelectedText = string.Empty;
            StatusText.Text = UiLanguage.T(e.Command == ApplicationCommands.Cut ? "Selected text cut." : "Selected text copied.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { DialogGuard.Show(this, UiLanguage.T("The clipboard is busy. Try again.")); }
        DocumentText.Focus();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DocumentText.Focus();
        SyncAccessibilityMenu();
        var runtime = _diagnostics.Check();
        if (!runtime.CoreOcrReady) StatusText.Text = runtime.Summary;
        if (_settings.CheckUpdatesOnStartup && !string.IsNullOrWhiteSpace(_settings.UpdateFeedUrl))
            await UpdateApplicationAsync(false);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { StatusText.Text = "Wait for the current conversion to finish."; return; }
        var d = new OpenFileDialog { Filter = "Supported documents|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|PDF files|*.pdf|Image files|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|All files|*.*" };
        if (DialogGuard.Run(d.ShowDialog) == true)
        {
            if (!await ConfirmUnsavedTextAsync()) return;
            _currentFile = d.FileName;
            _lastTextSavePath = null;
            _lastRecognition = null;
            _lowConfidenceIndex = -1;
            DocumentText.Clear();
            _savedText = string.Empty;
            StatusText.Text = $"Opened {System.IO.Path.GetFileName(_currentFile)}. Ready for recognition.";
            DocumentText.Focus();
        }
    }

    private async void Recognize_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_currentFile is null) { DialogGuard.Show(this, ExperienceMessages.Get("openFirst", _settings.MessageLanguage), "TextBridge", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!await ConfirmUnsavedTextAsync()) return;
        _busy = true;
        StatusText.Text = ExperienceMessages.Get("started", _settings.MessageLanguage);
        try
        {
            var progress = new Progress<string>(m => StatusText.Text = m);
            _lastRecognition = await _ocr.RecognizeWithConfidenceAsync(_currentFile, _settings, progress);
            DocumentText.Text = _lastRecognition.PlainText;
            var firstLetter = DocumentText.Text.FirstOrDefault(char.IsLetter);
            var rtl = firstLetter >= '\u0600' && firstLetter <= '\u06ff';
            DocumentText.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            DocumentText.Language = System.Windows.Markup.XmlLanguage.GetLanguage(rtl ? "ar" : _settings.OcrLanguages == "fra" ? "fr" : "en");
            _lowConfidenceIndex = -1;
            StatusText.Text = ExperienceMessages.Get(string.IsNullOrWhiteSpace(DocumentText.Text) ? "noText" : "complete", _settings.MessageLanguage);
            DocumentText.CaretIndex = 0;
            DocumentText.ScrollToHome();
            DocumentText.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Text = ExperienceMessages.Get("failed", _settings.MessageLanguage);
            DocumentText.Focus();
            DialogGuard.Show(this, ex.Message, "OCR Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _busy = false; }
    }

    private async void OpenMultiple_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var d = new OpenFileDialog { Multiselect = true, Filter = "Supported documents|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|All files|*.*" };
        if (DialogGuard.Run(d.ShowDialog) != true || d.FileNames.Length == 0) return;
        var folder = new Microsoft.Win32.OpenFolderDialog { Title = "Choose batch output folder", Multiselect = false };
        if (DialogGuard.Run(folder.ShowDialog) != true) return;
        var output = folder.FolderName;
        _busy = true;
        try
        {
            var batch = new BatchService(); var progress = new Progress<string>(m => StatusText.Text = m);
            var count = await batch.ConvertToTextAsync(d.FileNames, output, _settings, progress);
            StatusText.Text = $"Batch conversion complete. {count} files exported to {output}.";
            DocumentText.Focus();
        }
        catch (Exception ex) { DialogGuard.Show(this, ex.Message, "Batch OCR Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _busy = false; }
    }

    private async void OpenAiCorrection_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var key = new CredentialService().Load(_settings.ApiProvider) ?? string.Empty;
        if (ApiCorrectionService.ConfigurationError(_settings, key) is { } error)
        {
            if (DialogGuard.Show(this, error + "\n\nOpen API settings now? After saving, this correction request will continue and ask for confirmation before sending text.", "API correction setup", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var settingsWindow = new ApiSettingsWindow(_settings) { Owner = this };
            if (ShowDialogReturningFocus(settingsWindow) != true) return;
            key = new CredentialService().Load(_settings.ApiProvider) ?? string.Empty;
            if (ApiCorrectionService.ConfigurationError(_settings, key) is { } remainingError)
            {
                StatusText.Text = "API settings saved, but correction has not started. " + remainingError;
                DialogGuard.Show(this, StatusText.Text, "API correction setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        var selected = DocumentText.SelectionLength > 0;
        var start = selected ? DocumentText.SelectionStart : 0;
        var original = selected ? DocumentText.SelectedText : DocumentText.Text;
        if (string.IsNullOrWhiteSpace(original) || original.Length > OpenAiCorrectionService.MaximumCharacters)
        {
            DialogGuard.Show(this, $"Recognize a document or enter text, then select up to {OpenAiCorrectionService.MaximumCharacters:N0} characters. With no selection, the whole editor is sent.", "API correction");
            return;
        }
        if (DialogGuard.Show(this, $"Send {(selected ? "the selected" : "all editor")} text ({original.Length:N0} characters) to {_settings.ApiProvider} using your API account and model {ApiProviderCatalog.Model(_settings, _settings.ApiProvider)}? API charges may apply. No images or file paths will be sent. You will review the result before applying it. {ApiProviderCatalog.Notice(_settings.ApiProvider)}", "Send text to API provider?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _busy = true; DocumentText.IsReadOnly = true; CancelApiMenu.IsEnabled = true;
        using var cancellation = new CancellationTokenSource(); _apiCancellation = cancellation;
        StatusText.Text = "Requesting API correction. Use OCR > Cancel API request to cancel. Local text is preserved.";
        try
        {
            var corrected = await new ApiCorrectionService().CorrectAsync(original, _settings, key, true, cancellation.Token);
            var review = new ApiCorrectionReviewWindow(original, corrected) { Owner = this };
            if (ShowDialogReturningFocus(review) == true)
            {
                DocumentText.IsReadOnly = false;
                ApplyApiCorrection(start, original.Length, corrected);
                StatusText.Text = "API correction applied. Review the text; Undo restores the original.";
            }
            else StatusText.Text = "API correction discarded. Original local text retained.";
        }
        catch (OperationCanceledException) { StatusText.Text = "API request cancelled. Original local text retained."; }
        catch (Exception ex)
        {
            StatusText.Text = "API correction failed. Original local text retained; offline OCR remains available.";
            DialogGuard.Show(this, ex.Message, "API correction", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _apiCancellation = null; CancelApiMenu.IsEnabled = false; DocumentText.IsReadOnly = false; _busy = false; DocumentText.Focus();
        }
    }

    internal void ApplyApiCorrection(int start, int length, string corrected)
    {
        DocumentText.BeginChange();
        try { DocumentText.Select(start, length); DocumentText.SelectedText = corrected; }
        finally { DocumentText.EndChange(); }
        _lastRecognition = null; _lowConfidenceIndex = -1;
    }

    private void CancelApi_Click(object sender, RoutedEventArgs e) => _apiCancellation?.Cancel();

    private void ApiSettings_Click(object sender, RoutedEventArgs e)
    {
        var w = new ApiSettingsWindow(_settings) { Owner = this };
        var saved = ShowDialogReturningFocus(w);
        if (saved == true) StatusText.Text = _settings.OfflineOnlyMode ? "API settings saved. Offline only mode is enabled." : "API settings saved. No correction has run. To correct editor text, choose OCR > Correct text with selected API.";
    }

    private void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_settings) { Owner = this };
        var saved = ShowDialogReturningFocus(w);
        if (saved == true) { UiLanguage.Configure(_settings); UiLanguage.Apply(this); SyncAccessibilityMenu(); StatusText.Text = UiLanguage.T("Settings saved."); }
    }

    private bool? ShowDialogReturningFocus(Window window)
    {
        var previous = Keyboard.FocusedElement as UIElement;
        var result = DialogGuard.ShowDialog(window);
        if (previous is not null && previous.Focusable && previous.IsEnabled && previous.IsVisible && previous.Focus()) return result;
        DocumentText.Focus();
        return result;
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem m && m.Tag is string lang)
        {
            _settings.OcrLanguages = lang; _settings.Save(); SyncAccessibilityMenu();
            StatusText.Text = $"OCR language profile set to {m.Header}.";
        }
    }

    private async void SaveText_Click(object sender, RoutedEventArgs e)
    {
        await SaveDocumentTextAsync();
    }

    private async Task<bool> SaveDocumentTextAsync(bool saveAs = false)
    {
        var path = saveAs ? null : _lastTextSavePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var d = new SaveFileDialog { Filter = "Text file|*.txt", DefaultExt = ".txt", FileName = SuggestedName("txt") };
            if (DialogGuard.Run(d.ShowDialog) != true) return false;
            path = d.FileName;
        }
        try
        {
            var text = DocumentText.Text;
            await _export.SaveTextAsync(path, text);
            _lastTextSavePath = path;
            _savedText = text;
            StatusText.Text = $"Saved {System.IO.Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex) { ShowExportError(ex); return false; }
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveDocumentTextAsync(saveAs: true);
    }

    private void ExportWord_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "Word document|*.docx", DefaultExt = ".docx", FileName = SuggestedName("docx") };
        if (DialogGuard.Run(d.ShowDialog) != true) return;
        try
        {
            _export.SaveAccessibleDocx(d.FileName, DocumentText.Text, _currentFile is null ? "Recognized document" : System.IO.Path.GetFileNameWithoutExtension(_currentFile), _settings.PreserveTextLayout);
            StatusText.Text = $"Exported accessible Word document {System.IO.Path.GetFileName(d.FileName)}.";
        }
        catch (Exception ex) { ShowExportError(ex); }
    }


    private async void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "HTML document|*.html", DefaultExt = ".html", FileName = SuggestedName("html") };
        if (DialogGuard.Run(d.ShowDialog) != true) return;
        try
        {
            await _export.SaveHtmlAsync(d.FileName, DocumentText.Text, _currentFile is null ? "Recognized document" : System.IO.Path.GetFileNameWithoutExtension(_currentFile), _settings.PreserveTextLayout);
            StatusText.Text = $"Exported accessible HTML {System.IO.Path.GetFileName(d.FileName)}.";
        }
        catch (Exception ex) { ShowExportError(ex); }
    }

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "Markdown document|*.md", DefaultExt = ".md", FileName = SuggestedName("md") };
        if (DialogGuard.Run(d.ShowDialog) != true) return;
        try
        {
            await _export.SaveMarkdownAsync(d.FileName, DocumentText.Text, _currentFile is null ? "Recognized document" : System.IO.Path.GetFileNameWithoutExtension(_currentFile), _settings.PreserveTextLayout);
            StatusText.Text = $"Exported Markdown {System.IO.Path.GetFileName(d.FileName)}.";
        }
        catch (Exception ex) { ShowExportError(ex); }
    }

    private void ShowExportError(Exception ex)
    {
        StatusText.Text = "Export failed. Choose a writable location and try again.";
        DialogGuard.Show(this, ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ReviewRecognition_Click(object sender, RoutedEventArgs e)
    {
        var items = _lastRecognition is not null && _lastRecognition.Words.Count > 0
            ? _lastRecognition.LowConfidenceWords(_settings.LowConfidenceThreshold)
                .Select(w => new LowConfidenceItem(w.Text, w.Confidence, Math.Max(1, w.Line))).ToList()
            : _review.FindSuspiciousText(DocumentText.Text).ToList();
        if (items.Count == 0)
        {
            DialogGuard.Show(this, "No obvious suspicious text was found by the basic review rules. This does not guarantee that OCR is error-free.", "Recognition Review", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var w = new RecognitionReviewWindow(items, (line, text) =>
        {
            var index = DocumentText.Text.IndexOf(text, StringComparison.Ordinal);
            if (index >= 0) { DocumentText.Focus(); DocumentText.Select(index, text.Length); DocumentText.ScrollToLine(Math.Max(0, line - 1)); }
        }) { Owner = this };
        ShowDialogReturningFocus(w);
    }

    private void Find_Click(object sender, RoutedEventArgs e) => ShowDialogReturningFocus(new FindReplaceWindow(DocumentText, false) { Owner = this });
    private void Replace_Click(object sender, RoutedEventArgs e) => ShowDialogReturningFocus(new FindReplaceWindow(DocumentText, true) { Owner = this });

    private void SyncAccessibilityMenu()
    {
        void MarkLanguages(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<MenuItem>())
            {
                if (item.Tag is string profile && new[] { "ara", "eng", "fra", "ara+eng", "ara+fra", "eng+fra", "ara+eng+fra" }.Contains(profile))
                { item.IsCheckable = true; item.IsChecked = profile == _settings.OcrLanguages; }
                if (item.HasItems) MarkLanguages(item);
            }
        }
        MarkLanguages(ApplicationMenu);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(StatusText, _settings.AnnounceOcrProgress ? System.Windows.Automation.AutomationLiveSetting.Polite : System.Windows.Automation.AutomationLiveSetting.Off);
        StatusText.Language = System.Windows.Markup.XmlLanguage.GetLanguage(ExperienceMessages.Language(_settings.MessageLanguage));
        // UI Automation and system contrast remain available to every user.
        ReadingAppearance.Apply(this, _settings);
        ReadingAppearance.ApplyDocument(DocumentText, _settings);
        ApplicationStatusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        ShowStatusBarMenu.IsChecked = _settings.ShowStatusBar;
    }
    private string SuggestedName(string ext) => (_currentFile is null ? "recognized-document" : System.IO.Path.GetFileNameWithoutExtension(_currentFile)) + "." + ext;
    private void Help_Click(object s, RoutedEventArgs e) => OpenDoc("help/index.html");
    private void Shortcuts_Click(object s, RoutedEventArgs e) => OpenDoc("help/shortcuts.html");
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await UpdateApplicationAsync(true);

    private async Task UpdateApplicationAsync(bool showResult)
    {
        if (_busy || _updating || _closing) return;
        _updating = true;
        var wasEnabled = DocumentText.IsEnabled;
        var lockedForInstall = false;
        try
        {
            StatusText.Text = UiLanguage.T("Checking for updates.");
            var result = await _updates.CheckAsync(_settings.UpdateFeedUrl, AppVersion);
            if (_closing || _allowClose) return;
            StatusText.Text = UiLanguage.T(result.Message);
            if (!result.UpdateAvailable)
            {
                if (showResult) DialogGuard.Show(this, result.Message, "TextBridge Updates");
                return;
            }
            var consent = DialogGuard.Show(this,
                UiLanguage.T("Version {0} is available. Download and install it now? Your work will be saved or reviewed before installation. Windows may ask for administrator permission.").Replace("{0}", result.LatestVersion),
                UiLanguage.T("TextBridge Updates"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (consent != MessageBoxResult.Yes) { StatusText.Text = UiLanguage.T("Update cancelled. Nothing was downloaded or installed."); return; }
            StatusText.Text = UiLanguage.T("Downloading update.");
            var installer = await _updates.DownloadAsync(result);
            if (_closing || _allowClose) return;
            if (_busy || _apiCancellation is not null) { StatusText.Text = UiLanguage.T("Update downloaded. Check again after the current operation finishes."); return; }
            _busy = true;
            lockedForInstall = true;
            DocumentText.IsEnabled = false;
            ApplicationMenu.IsEnabled = false;
            if (!await ConfirmUnsavedTextAsync()) return;
            StatusText.Text = UiLanguage.T("Installing update.");
            var start = UpdateInstaller.CreateStartInfo(installer, AppContext.BaseDirectory,
                UpdateInstaller.IsCurrentUserInstall(AppContext.BaseDirectory));
            if (Process.Start(start) is null) throw new IOException("The installer could not be started.");
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            if (!_closing) DialogGuard.Show(this, UiLanguage.T("The update could not be installed. Your document is unchanged.") + "\n" + ex.Message, "TextBridge Updates", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { if (lockedForInstall) _busy = false; _updating = false; DocumentText.IsEnabled = wasEnabled; ApplicationMenu.IsEnabled = true; }
    }

    private async void ExportSearchablePdf_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_currentFile is null) { DialogGuard.Show(this, ExperienceMessages.Get("openFirst", _settings.MessageLanguage), "TextBridge", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var d = new SaveFileDialog { Filter = "Searchable PDF|*.pdf", DefaultExt = ".pdf", FileName = SuggestedName("searchable.pdf") };
        if (DialogGuard.Run(d.ShowDialog) != true) return;
        _busy = true;
        try { var progress = new Progress<string>(m => StatusText.Text = m); await _searchablePdf.CreateAsync(_currentFile, d.FileName, _settings, progress); StatusText.Text = "Searchable PDF created."; }
        catch (Exception ex) { DialogGuard.Show(this, ex.Message, "Searchable PDF Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _busy = false; }
    }

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => ShowDialogReturningFocus(new ReportProblemWindow(_settings) { Owner = this });

    private void RuntimeCheck_Click(object sender, RoutedEventArgs e)
    {
        var r = _diagnostics.Check(_settings);
        DialogGuard.Show(this, r.Details, "Runtime Diagnostics", MessageBoxButton.OK, r.CoreOcrReady ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void AccessibilityGuide_Click(object sender, RoutedEventArgs e) => OpenDoc("help/accessibility.html");

    private void LinearizeTable_Click(object sender, RoutedEventArgs e)
    {
        var selected = DocumentText.SelectedText;
        if (string.IsNullOrWhiteSpace(selected))
        {
            DialogGuard.Show(this, "Select the table text first. Tab-separated, pipe-separated, or aligned columns are supported.", "Linearize Table", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var linear = _tables.Linearize(selected);
        DocumentText.SelectedText = linear;
        StatusText.Text = "Selected table text was converted to an accessible linear representation.";
    }

    private void NextLowConfidence_Click(object sender, RoutedEventArgs e) => MoveLowConfidence(1);
    private void PreviousLowConfidence_Click(object sender, RoutedEventArgs e) => MoveLowConfidence(-1);
    private void MoveLowConfidence(int direction)
    {
        var words = _lastRecognition?.ReviewLocations(DocumentText.Text, _settings.LowConfidenceThreshold);
        if (words is null || words.Count == 0)
        {
            StatusText.Text = UiLanguage.T("No text to check was found. If you edited the text, recognize the document again to refresh review locations.");
            return;
        }
        _lowConfidenceIndex = _lowConfidenceIndex < 0
            ? (direction < 0 ? words.Count - 1 : 0)
            : (_lowConfidenceIndex + direction + words.Count) % words.Count;
        var (word, start) = words[_lowConfidenceIndex];
        DocumentText.Focus();
        DocumentText.Select(start, word.Text.Length);
        DocumentText.ScrollToLine(DocumentText.GetLineIndexFromCharacterIndex(start));
        StatusText.Text = UiLanguage.T($"Text to check {_lowConfidenceIndex + 1} of {words.Count}: {word.Text}. Recognition confidence: {word.Confidence:0} percent.");
    }

    private void FocusDocument_Click(object sender, RoutedEventArgs e) => DocumentText.Focus();
    private void FocusStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ShowStatusBar) StatusText.Focus();
        else DialogGuard.Show(this, StatusText.Text, UiLanguage.T("Current status"));
    }

    private void ShowStatusBar_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowStatusBarMenu.IsChecked && StatusText.IsKeyboardFocusWithin) DocumentText.Focus();
        _settings.ShowStatusBar = ShowStatusBarMenu.IsChecked;
        SyncAccessibilityMenu();
        _settings.Save();
    }

    private void ReadingAppearance_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        window.OpenLowVision();
        if (ShowDialogReturningFocus(window) == true)
        { UiLanguage.Configure(_settings); UiLanguage.Apply(this); SyncAccessibilityMenu(); }
    }

    private bool IsMenuActive => ApplicationMenu.IsKeyboardFocusWithin ||
        ApplicationMenu.Items.OfType<MenuItem>().Any(item => item.IsSubmenuOpen);

    private void LeaveMenu()
    {
        foreach (var item in ApplicationMenu.Items.OfType<MenuItem>()) item.IsSubmenuOpen = false;
        if (_menuReturnFocus is not null && _menuReturnFocus.IsVisible && _menuReturnFocus.IsEnabled && _menuReturnFocus.Focus()) return;
        DocumentText.Focus();
    }

    internal void CloseMenuLevel()
    {
        MenuItem? deepest = null;
        void Visit(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<MenuItem>().Where(item => item.IsSubmenuOpen))
            { deepest = item; Visit(item); }
        }
        Visit(ApplicationMenu);
        if (deepest is null) { LeaveMenu(); return; }
        deepest.IsSubmenuOpen = false;
        deepest.Focus();
    }

    private void ToggleMenu()
    {
        if (IsMenuActive) { LeaveMenu(); return; }
        _menuReturnFocus = Keyboard.FocusedElement as UIElement;
        FileMenu.Focus();
    }

    private void ApplicationMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Tab stays within menu navigation; Escape closes one submenu level.
        if (e.Key == Key.Tab) e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftAlt or Key.RightAlt) || !_altPressed) return;
        var toggle = !_altUsedWithOtherKey;
        _altPressed = false;
        _altUsedWithOtherKey = false;
        if (toggle) ToggleMenu();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            // Preserve AltGr (Ctrl+Alt) and screen-reader modifier combinations.
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) != 0) return;
            if (!e.IsRepeat) { _altPressed = true; _altUsedWithOtherKey = false; }
            e.Handled = true;
            return;
        }
        if (_altPressed) _altUsedWithOtherKey = true;
        // Tab visits the document and status; only Alt/F10/mnemonics enter the menu.
        if (key == Key.Tab && !IsMenuActive &&
            (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            if (_settings.ShowStatusBar && DocumentText.IsKeyboardFocusWithin) StatusText.Focus();
            else DocumentText.Focus();
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && IsMenuActive)
        {
            CloseMenuLevel(); e.Handled = true; return;
        }
        if (key == Key.F10 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ToggleMenu(); e.Handled = true; return;
        }
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && e.Key == Key.O) { Open_Click(sender, e); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.S) { SaveAs_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { SaveText_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { Find_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.H) { Replace_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.R) { Recognize_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.E) { ExportWord_Click(sender, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.OemComma) { Preferences_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.Shift) { FocusStatus_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None) { FocusDocument_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F1) { Help_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F4 && shift) { PreviousLowConfidence_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F4) { NextLowConfidence_Click(sender, e); e.Handled = true; }
    }

    private void About_Click(object s, RoutedEventArgs e) => ShowDialogReturningFocus(new AuthorInformationWindow { Owner = this });
    private void OpenDoc(string rel) { if (UiLanguage.IsArabic) rel = rel.Replace("help/", "help/ar/"); var p = System.IO.Path.Combine(AppContext.BaseDirectory, rel); if (System.IO.File.Exists(p)) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); else DialogGuard.Show(this, "Help file is not installed yet."); }
    private async void Exit_Click(object s, RoutedEventArgs e) { await CloseWithFarewellAsync(); }
    private async void Window_Closing(object? s, CancelEventArgs e) { if (_allowClose) return; e.Cancel = true; await CloseWithFarewellAsync(); }
    private async Task<bool> ConfirmUnsavedTextAsync()
    {
        if (!HasUnsavedText) return true;
        var answer = DialogGuard.Show(this,
            ExperienceMessages.Get("unsaved", _settings.MessageLanguage),
            "TextBridge — Unsaved text", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveDocumentTextAsync() && !HasUnsavedText);
    }

    private async Task CloseWithFarewellAsync()
    {
        if (_allowClose || _closing) return;
        if (_busy) { StatusText.Text = "Wait for the current conversion to finish before closing."; return; }
        _closing = true;
        try
        {
            if (!await ConfirmUnsavedTextAsync()) return;
            IsEnabled = false;
            StatusText.Text = ExperienceMessages.Get("goodbye", _settings.MessageLanguage);
            await _welcome.PlayExitAsync(this, _settings.PlayExitSound, _settings.MessageLanguage);
            _allowClose = true;
            Close();
        }
        finally { _closing = false; }
    }
}










