using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Media;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private Control? _accessibilityTarget;
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent(); _s = settings; UpdateFeed.Text = _s.UpdateFeedUrl;
        AnnounceProgress.IsChecked = _s.AnnounceOcrProgress;
        SelectByTag(AnnouncementInterval, AppSettings.NormalizeProgressInterval(_s.ProgressAnnouncementSeconds).ToString(CultureInfo.InvariantCulture));
        ShowStatusBar.IsChecked = _s.ShowStatusBar;
        SelectByTag(DocumentFont, _s.DocumentFontFamily);
        DocumentSize.Text = _s.DocumentFontSize.ToString(CultureInfo.CurrentCulture);
        InterfaceSize.Text = _s.InterfaceFontSize.ToString(CultureInfo.CurrentCulture);
        BoldDocument.IsChecked = _s.DocumentBold;
        SelectByTag(ReadingColors, _s.ReadingColors);
        UpdatePreview();
        AccessibleReading.IsChecked = _s.AccessibleReadingMode;
        StartupSound.IsChecked = _s.PlayStartupSound; ExitSound.IsChecked = _s.PlayExitSound; WelcomeMessage.IsChecked = _s.ShowWelcomeMessage;
        PreserveLayout.IsChecked = _s.PreserveTextLayout; PreferDigital.IsChecked = _s.PreferDigitalPdfText; AutoRotate.IsChecked = _s.AutoRotate; Deskew.IsChecked = _s.Deskew; Grayscale.IsChecked = _s.Grayscale; CheckUpdates.IsChecked = _s.CheckUpdatesOnStartup;
        SelectByTag(InterfaceLanguage, _s.InterfaceLanguage);
        SelectByTag(MessageLanguage, _s.MessageLanguage);
        SelectByTag(Languages, _s.OcrLanguages);
        foreach(var item in OcrEngine.Items.OfType<ComboBoxItem>())
            if((string?)item.Tag=="paddle") item.IsEnabled=OcrBackendService.PaddleInstalled;
        SelectByTag(OcrEngine,OcrBackendService.ResolveEngine(_s));
        OcrEngine.SelectionChanged+=(_,_)=>UpdateEngineHint();
        LiveRegionAnnouncements.Attach(EngineHint);
        UpdateEngineHint();
        Loaded += (_, _) => FocusAccessibilityTarget();
    }
    private void UpdateEngineHint()
    {
        var windows=(OcrEngine.SelectedItem as ComboBoxItem)?.Tag?.ToString()=="windows";
        AutoRotate.IsEnabled=!windows;
        EngineHint.Text=windows
            ? UiLanguage.T("Windows OCR works offline with installed Windows language packs. Recognition confidence is unavailable.")
            : UiLanguage.T("PaddleOCR works offline with its bundled language models. The first recognition may take longer.");
        System.Windows.Automation.AutomationProperties.SetHelpText(OcrEngine, EngineHint.Text);
    }
    private static void SelectByTag(ComboBox box, string value)
    { foreach (var item in box.Items.OfType<ComboBoxItem>()) if ((string?)item.Tag == value) { box.SelectedItem = item; return; } box.SelectedIndex = 0; }
    internal void OpenAccessibility() => SelectAccessibilityGroup(AnnounceProgress);
    internal void OpenLowVision() => SelectAccessibilityGroup(DocumentFont);
    private void SelectAccessibilityGroup(Control target)
    {
        Tabs.SelectedItem = AccessibilityTab;
        _accessibilityTarget = target;
        if (IsLoaded) FocusAccessibilityTarget();
    }
    private void FocusAccessibilityTarget()
    {
        if (_accessibilityTarget is null) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (!IsLoaded || Tabs.SelectedItem != AccessibilityTab || _accessibilityTarget is null) return;
            _accessibilityTarget.BringIntoView();
            _accessibilityTarget.Focus();
        }));
    }
    private void ScreenReaderOptions_Click(object sender, RoutedEventArgs e) => OpenAccessibility();
    private void LowVisionOptions_Click(object sender, RoutedEventArgs e) => OpenLowVision();
    private void SoundOptions_Click(object sender, RoutedEventArgs e) => SelectAccessibilityGroup(StartupSound);
    private void Appearance_Changed(object sender, RoutedEventArgs e) => UpdatePreview();
    private void UpdatePreview()
    {
        if (AppearancePreview is null || DocumentSize is null || ReadingColors is null || BoldDocument is null) return;
        AppearancePreview.FontFamily = new FontFamily((DocumentFont.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Segoe UI");
        var sizeText = (DocumentSize.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DocumentSize.Text;
        AppearancePreview.FontSize = double.TryParse(sizeText, out var size) ? ReadingAppearance.Size(size, 18, 10, 72) * 96 / 72 : 24;
        AppearancePreview.FontWeight = BoldDocument.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        var theme = SystemParameters.HighContrast ? "system" : (ReadingColors.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        var (background, foreground) = ReadingAppearance.Colors(theme);
        AppearancePreview.Background = background;
        AppearancePreview.Foreground = foreground;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(UpdateFeed.Text) && !UpdateService.IsHttps(UpdateFeed.Text.Trim()))
        { DialogGuard.Show(this, "The update feed must use HTTPS."); return; }
        if (!double.TryParse(DocumentSize.Text, out var documentSize) || !double.IsFinite(documentSize) || documentSize < 10 || documentSize > 72 ||
            !double.TryParse(InterfaceSize.Text, out var interfaceSize) || !double.IsFinite(interfaceSize) || interfaceSize < 12 || interfaceSize > 32)
        { DialogGuard.Show(this, UiLanguage.T("Choose a document size from 10 to 72 and an interface size from 12 to 32.")); return; }
        var chosenEngine = (OcrEngine.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var chosenLanguages = (Languages.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "eng";
        if (chosenEngine == "paddle" && PaddleOcrService.RequiredModels(chosenLanguages, AutoRotate.IsChecked == true).Any(model => !PaddleOcrService.ModelInstalled(model)))
        { DialogGuard.Show(this, UiLanguage.T("The selected language model is not installed. Choose an installed OCR language or rerun Custom setup to add it.")); return; }
        _s.ShowStatusBar = ShowStatusBar.IsChecked == true;
        _s.DocumentFontFamily = (DocumentFont.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Segoe UI";
        _s.DocumentFontSize = documentSize;
        _s.InterfaceFontSize = interfaceSize;
        _s.DocumentBold = BoldDocument.IsChecked == true;
        _s.ReadingColors = (ReadingColors.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        _s.UpdateFeedUrl = UpdateFeed.Text.Trim();
        _s.InterfaceLanguage = (InterfaceLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        _s.MessageLanguage = (MessageLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        _s.ScreenReaderOptimization = true; _s.AnnounceOcrProgress = AnnounceProgress.IsChecked == true;
        _s.ProgressAnnouncementSeconds = AppSettings.NormalizeProgressInterval(int.Parse((AnnouncementInterval.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "10", CultureInfo.InvariantCulture));
        _s.AccessibleReadingMode = AccessibleReading.IsChecked == true; _s.FollowWindowsHighContrast = true;
        _s.PlayStartupSound = StartupSound.IsChecked == true; _s.PlayExitSound = ExitSound.IsChecked == true; _s.ShowWelcomeMessage = WelcomeMessage.IsChecked == true;
        _s.PreserveTextLayout = PreserveLayout.IsChecked == true; _s.PreferDigitalPdfText = PreferDigital.IsChecked == true; _s.AutoRotate = AutoRotate.IsChecked == true; _s.Deskew = Deskew.IsChecked == true; _s.Grayscale = Grayscale.IsChecked == true; _s.CheckUpdatesOnStartup = CheckUpdates.IsChecked == true;
        _s.OcrLanguages = (Languages.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ara+eng+fra";
        _s.OcrEngine = (OcrEngine.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        _s.Save(); DialogResult = true;
    }
}




