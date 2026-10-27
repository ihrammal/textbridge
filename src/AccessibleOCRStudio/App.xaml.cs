using System.Windows;
namespace AccessibleOCRStudio;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        ToastAnnouncements.RegisterBranding();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(FitWindowToDisplay));
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        if(e.Args.Length==4 && e.Args[0]=="--verify-ocr")
        {
            try
            {
                var settings=new Services.AppSettings { OcrEngine=Services.OcrBackendService.DefaultEngine,OcrLanguages=e.Args[2],Deskew=false,AutoRotate=false };
                var text=await new Services.LocalOcrService().RecognizeAsync(e.Args[1],settings);
                await new Services.SearchablePdfService().CreateAsync(e.Args[1],e.Args[3]+".pdf",settings);
                await System.IO.File.WriteAllTextAsync(e.Args[3],System.Text.Json.JsonSerializer.Serialize(new { engine=Services.OcrBackendService.ResolveEngine(settings),text }));
                Shutdown(string.IsNullOrWhiteSpace(text)?2:0);
            }
            catch(Exception exception)
            {
                await System.IO.File.WriteAllTextAsync(e.Args[3],System.Text.Json.JsonSerializer.Serialize(new { error=exception.Message }));
                Shutdown(1);
            }
            return;
        }
        try
        {
            var settings = Services.AppSettings.Load();
            Services.UiLanguage.Configure(settings);
            if (!await new Services.WelcomeService().ShowStartupAsync(settings)) { Shutdown(); return; }
            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
            if (e.Args.Length == 1) main.OpenStartupFile(e.Args[0]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TextBridge — Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void FitWindowToDisplay(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || !ReferenceEquals(e.OriginalSource, window)) return;
        Services.UiLanguage.Apply(window);
        Services.ReadingAppearance.Apply(window, Services.AppSettings.Load());
        var area = SystemParameters.WorkArea;
        window.MinWidth = Math.Min(window.MinWidth, area.Width);
        window.MinHeight = Math.Min(window.MinHeight, area.Height);
        if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, area.Width);
        if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, area.Height);
    }
}

