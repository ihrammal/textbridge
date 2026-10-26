using System.IO;
using System.Windows;
using System.Windows.Controls;
using AccessibleOCRStudio;
using AccessibleOCRStudio.Services;

internal static class ProgressChecks
{
    internal static void Run(string temp, Action<bool, string> check)
    {
        var cadence = new OcrAnnouncementCadence();
        check(!cadence.Due(TimeSpan.FromSeconds(9), 10, true) && cadence.Due(TimeSpan.FromSeconds(10), 10, false),
            "Timed progress speaks at the chosen interval even without a completed page");
        check(!cadence.Due(TimeSpan.FromSeconds(19), 10, false) && cadence.Due(TimeSpan.FromSeconds(20), 10, false),
            "A slow page continues to produce periodic announcements rather than only one message");
        check(!cadence.Due(TimeSpan.FromSeconds(100), 0, false) && cadence.Due(TimeSpan.FromSeconds(100), 0, true),
            "Every-page mode announces completed pages without pretending to have timed updates");
        check(AppSettings.NormalizeProgressInterval(-1) == 10 && AppSettings.NormalizeProgressInterval(60) == 60,
            "Invalid progress frequency falls back to ten seconds");
        var settingsPath = Path.Combine(temp, "progress-settings.json");
        File.WriteAllText(settingsPath, "{}");
        check(AppSettings.Load(settingsPath).ProgressAnnouncementSeconds == 10,
            "Older settings files gain a safe announcement frequency");
        new AppSettings { ProgressAnnouncementSeconds = 30 }.Save(settingsPath);
        check(AppSettings.Load(settingsPath).ProgressAnnouncementSeconds == 30,
            "The chosen announcement frequency survives saving and loading");
        var status = new OcrProgress(OcrStage.Recognizing, 3, 17, 2);
        check(status.Message("en").Contains("Page 3 of 17") && Math.Abs(status.Percent - 200.0 / 17) < .001,
            "Page counts and percent are derived from the document, not fixed example values");
        check(status.Message("ar").Contains("الصفحة 3 من 17") && status.Message("fr").Contains("Page 3 sur 17"),
            "Progress supports Arabic, English and French message languages");
        check(!new OcrProgress(OcrStage.Inspecting, 0, 0, 0).Message("en").Contains("percent"),
            "Unknown page counts never claim a made-up percentage");

        var editor = new TextBox { Text = "Saved earlier result", IsReadOnly = true };
        var stream = new RecognitionTextStream(editor, "ara");
        check(editor.Text == "Saved earlier result" && !stream.HasChunks,
            "A failure before the first result does not clear previous editor text");
        var first = OcrProgress.Chunk("مرحبا بالعالم", 1, 3, true, "en");
        stream.Append(new(OcrStage.PageCompleted, 1, 3, 1, first, "مرحبا بالعالم"));
        check(editor.Text.StartsWith("--- Page 1 of 3 ---") && editor.FlowDirection == FlowDirection.RightToLeft,
            "The first page appears immediately and English page labels do not break Arabic text direction");
        var selection = editor.Text.IndexOf("مرحبا", StringComparison.Ordinal);
        editor.Select(selection, 5);
        stream.Append(new(OcrStage.PageCompleted, 2, 3, 2, OcrProgress.Chunk("Second page", 2, 3, true, "en"), "Second page"));
        check(editor.Text.Contains("--- Page 2 of 3 ---") && editor.SelectedText == "مرحبا" && editor.IsReadOnly,
            "Appending a later page preserves selection and does not enable editing during recognition");
        check(stream.HasText && stream.HasChunks && !editor.Text.Contains("Saved earlier result"),
            "Partial results remain available independently of whether the whole document succeeds");
        var blankEditor = new TextBox();
        var blankStream = new RecognitionTextStream(blankEditor, "eng");
        blankStream.Append(new(OcrStage.PageCompleted, 1, 1, 1, OcrProgress.Chunk("", 1, 1, true, "en"), ""));
        check(!blankStream.HasText && blankEditor.Text.Contains("Page 1 of 1"),
            "An empty page keeps its page number without falsely claiming recognized text");
        var review = new RecognitionDocument { PlainText = "--- Page 1 of 1 ---\nPage content" };
        review.ContentStart = review.PlainText.IndexOf('\n') + 1;
        review.Words.Add(new("Page", 30, 1, 1, 0, 0, 20, 10));
        check(review.ReviewLocations(review.PlainText, 70).Single().Start == review.ContentStart,
            "Confidence review skips the generated page heading when matching recognized words");
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
        var preferences = new SettingsWindow(new AppSettings { ProgressAnnouncementSeconds = 15 });
        var options = (ComboBox)preferences.FindName("AnnouncementInterval");
        check(options.Items.Count == 6 && (string?)((ComboBoxItem)options.SelectedItem).Tag == "15",
            "Accessibility settings expose each-page and five timed announcement choices");
        ((CheckBox)preferences.FindName("AnnounceProgress")).IsChecked = false;
        check(!options.IsEnabled, "Speech frequency is unavailable when OCR announcements are switched off");
        ((CheckBox)preferences.FindName("AnnounceProgress")).IsChecked = true;
        check(options.IsEnabled, "Re-enabling OCR announcements restores the frequency control");
        preferences.Close();
    }
}
