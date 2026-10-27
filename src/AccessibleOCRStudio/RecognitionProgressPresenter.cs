using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;

internal sealed class RecognitionProgressPresenter : IProgress<OcrProgress>, IDisposable
{
    private readonly TextBlock status;
    private readonly TextBlock detail;
    private readonly ProgressBar bar;
    private readonly UIElement readerTarget;
    private readonly Func<AppSettings> settings;
    private readonly string language;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly OcrAnnouncementCadence cadence = new();
    private OcrProgress latest = new(OcrStage.Inspecting, 0, 0, 0);
    private bool stopped;

    internal RecognitionProgressPresenter(TextBlock status, TextBlock detail, ProgressBar bar,
        FrameworkElement panel, UIElement readerTarget, Func<AppSettings> settings, string language)
    {
        this.status = status; this.detail = detail; this.bar = bar;
        this.readerTarget = readerTarget; this.settings = settings; this.language = language;
        panel.Visibility = Visibility.Visible;
        bar.Value = 0; bar.IsIndeterminate = true;
        detail.Text = latest.Message(language);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += Tick;
        timer.Start();
    }

    internal string CurrentMessage => latest.Message(language);

    public void Report(OcrProgress update)
    {
        if (!status.Dispatcher.CheckAccess()) { status.Dispatcher.Invoke(() => Report(update)); return; }
        if (stopped) return;
        latest = update;
        var message = CurrentMessage;
        LiveRegionAnnouncements.SetSilently(status, message);
        detail.Text = message;
        bar.IsIndeterminate = update.TotalPages <= 0;
        bar.Value = update.Percent;
        Speak(update.Stage == OcrStage.PageCompleted);
    }

    private void Tick(object? sender, EventArgs e) => Speak(false);
    internal void Finish(string message)
    {
        Dispose();
        LiveRegionAnnouncements.SetSilently(status, message);
        detail.Text = message;
        if (settings().AnnounceOcrProgress) LiveRegionAnnouncements.Announce(detail, readerTarget, message);
    }

    private void Speak(bool pageCompleted)
    {
        var current = settings();
        if (!stopped && current.AnnounceOcrProgress && cadence.Due(elapsed.Elapsed, current.ProgressAnnouncementSeconds, pageCompleted))
            LiveRegionAnnouncements.Announce(detail, readerTarget, CurrentMessage);
    }

    public void Dispose()
    {
        if (stopped) return;
        stopped = true;
        timer.Stop(); timer.Tick -= Tick;
        bar.IsIndeterminate = false;
    }
}
