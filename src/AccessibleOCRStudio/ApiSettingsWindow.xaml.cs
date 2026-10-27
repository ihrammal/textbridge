using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;
public partial class ApiSettingsWindow : Window
{
    private bool _loadingProvider;
    private readonly AppSettings _settings;
    private readonly CredentialService _credentials;
    private readonly CancellationTokenSource _lifetime = new();
    public ApiSettingsWindow(AppSettings settings) : this(settings, new CredentialService()) { }
    internal ApiSettingsWindow(AppSettings settings, CredentialService credentials)
    {
        InitializeComponent(); _settings = settings; _credentials = credentials;
        LiveRegionAnnouncements.Attach(ApiStatus);
        LiveRegionAnnouncements.Attach(ProviderSelectionStatus);
        OfflineOnly.IsChecked = settings.OfflineOnlyMode;
        AllowContent.IsChecked = settings.AllowApiDocumentContent;
        OcrCorrection.IsChecked = settings.AllowApiOcrCorrection;
        ModelId.Text = ApiProviderCatalog.Model(settings, settings.ApiProvider);
        foreach (ComboBoxItem item in Provider.Items)
            if ((item.Tag?.ToString() ?? item.Content?.ToString() ?? "").Equals(settings.ApiProvider, StringComparison.OrdinalIgnoreCase)) { Provider.SelectedItem = item; break; }
        if (Provider.SelectedIndex < 0) Provider.SelectedIndex = 0;
        LoadProviderCredential();
        Provider.SelectionChanged += (_, _) => LoadProviderCredential();
        ApiKey.PasswordChanged += (_, _) => { if (!_loadingProvider) ApiStatus.Text = "Key changed. Use Test API key to check the current key and model. No document text is used."; };
        ModelId.TextChanged += (_, _) => { if (!_loadingProvider) ApiStatus.Text = "Model changed. Use Test API key to check the current key and model."; };
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); ApiKey.Clear(); };
    }
    private string SelectedProvider => ((Provider.SelectedItem as ComboBoxItem)?.Tag ?? (Provider.SelectedItem as ComboBoxItem)?.Content)?.ToString() ?? "None";
    private void LoadProviderCredential()
    {
        _loadingProvider = true;
        try {
        ApiKey.Password = SelectedProvider == "None" ? string.Empty : _credentials.Load(SelectedProvider) ?? string.Empty;
        ModelId.Text = ApiProviderCatalog.Model(_settings, SelectedProvider);
        ProviderNotice.Text = ApiProviderCatalog.Notice(SelectedProvider);
        ApiKeyLabel.Text = UiLanguage.T("Your " + SelectedProvider + " API key");
        CreateApiKey.IsEnabled = ApiProviderCatalog.IsSupported(SelectedProvider);
        CreateApiKey.Content = UiLanguage.T("Create " + SelectedProvider + " API _key (opens website)");
        TestApiKey.Content = UiLanguage.T("_Test " + SelectedProvider + " API key");
        TestApiKey.IsEnabled = ApiProviderCatalog.IsSupported(SelectedProvider);
        ApiStatus.Text = "Testing uses only a fixed short sample, never your document. Free quota or paid charges depend on your account plan.";
        } finally { _loadingProvider = false; }
        System.Windows.Automation.AutomationProperties.SetName(Provider, UiLanguage.T($"API provider: {SelectedProvider}"));
        ProviderSelectionStatus.Text = $"{SelectedProvider} selected.";
    }
    private AppSettings PendingSettings()
    {
        var pending = new AppSettings
        {
            OfflineOnlyMode = OfflineOnly.IsChecked == true, ApiProvider = SelectedProvider,
            OpenAiModel = _settings.OpenAiModel, GeminiModel = _settings.GeminiModel, GroqModel = _settings.GroqModel,
            AllowApiDocumentContent = AllowContent.IsChecked == true, AllowApiOcrCorrection = OcrCorrection.IsChecked == true
        };
        ApiProviderCatalog.SetModel(pending, SelectedProvider, ModelId.Text.Trim());
        return pending;
    }
    private void CreateApiKey_Click(object sender, RoutedEventArgs e)
    {
        // The labeled action is an explicit request to open the official site, even in offline mode.
        try { Process.Start(new ProcessStartInfo(ApiProviderCatalog.KeyUrl(SelectedProvider)) { UseShellExecute = true }); }
        catch { ApiStatus.Text = "Could not open the browser. Visit the selected provider website to create your own key."; ApiStatus.Focus(); }
    }
    private async void TestApiKey_Click(object sender, RoutedEventArgs e)
    {
        var settings = PendingSettings(); var key = ApiKey.Password;
        if (ApiCorrectionService.ConfigurationError(settings, key, false) is { } error) { ApiStatus.Text = error; ApiStatus.Focus(); return; }
        TestApiKey.IsEnabled = false; Provider.IsEnabled = false; ApiKey.IsEnabled = false; ModelId.IsEnabled = false;
        ApiStatus.Text = $"Testing {settings.ApiProvider} with a fixed short sample. No document text is sent.";
        try
        {
            await new ApiCorrectionService().TestConnectionAsync(settings, key, _lifetime.Token);
            ApiStatus.Text = "API key test succeeded: the selected provider returned a response using this key and model. Document correction also requires document-sharing permission, correction permission and confirmation for each request.";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { ApiStatus.Text = ex.Message; }
        finally { if (!_lifetime.IsCancellationRequested) { TestApiKey.IsEnabled = true; Provider.IsEnabled = true; ApiKey.IsEnabled = true; ModelId.IsEnabled = true; ApiStatus.Focus(); } }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var provider = SelectedProvider;
            if (provider != "None")
            {
                if (!string.IsNullOrWhiteSpace(ApiKey.Password)) _credentials.Save(provider, ApiKey.Password.Trim());
                else _credentials.Delete(provider);
            }
            var updated = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_settings))!;
            updated.OfflineOnlyMode = OfflineOnly.IsChecked == true;
            updated.AllowApiDocumentContent = AllowContent.IsChecked == true;
            updated.AllowApiOcrCorrection = OcrCorrection.IsChecked == true;
            updated.AllowImageDescription = false; updated.AllowTableReconstruction = false;
            updated.ApiProvider = provider; ApiProviderCatalog.SetModel(updated, provider, ModelId.Text.Trim());
            updated.Save();
            _settings.OfflineOnlyMode = updated.OfflineOnlyMode; _settings.AllowApiDocumentContent = updated.AllowApiDocumentContent;
            _settings.AllowApiOcrCorrection = updated.AllowApiOcrCorrection; _settings.AllowImageDescription = false; _settings.AllowTableReconstruction = false;
            _settings.ApiProvider = provider; _settings.OpenAiModel = updated.OpenAiModel; _settings.GeminiModel = updated.GeminiModel; _settings.GroqModel = updated.GroqModel; DialogResult = true;
        }
        catch { ApiStatus.Text = "Could not save API settings or the protected key. Check that your Windows profile is writable, then try again. Local OCR remains available."; ApiStatus.Focus(); }
    }
}






