using System.Globalization;

namespace AccessibleOCRStudio.Services;

public enum OcrStage { Inspecting, Rendering, Recognizing, PageCompleted }

public sealed record OcrProgress(OcrStage Stage, int Page, int TotalPages, int CompletedPages,
    string? TextChunk = null, string? PageText = null)
{
    public int FileNumber { get; init; }
    public int TotalFiles { get; init; }
    public double Percent => TotalPages <= 0 ? 0 : Math.Clamp(100.0 * CompletedPages / TotalPages, 0, 100);

    public string Message(string? language)
    {
        var lang = ExperienceMessages.Language(language);
        var culture = CultureInfo.GetCultureInfo(lang);
        var stage = (Stage, lang) switch
        {
            (OcrStage.Inspecting, "ar") => "جارٍ فحص المستند.",
            (OcrStage.Inspecting, "fr") => "Analyse du document.",
            (OcrStage.Inspecting, _) => "Inspecting document.",
            (OcrStage.Rendering, "ar") => "جارٍ تجهيز الصفحة.",
            (OcrStage.Rendering, "fr") => "Préparation de la page.",
            (OcrStage.Rendering, _) => "Preparing page.",
            (OcrStage.Recognizing, "ar") => "جارٍ استخراج النص.",
            (OcrStage.Recognizing, "fr") => "Reconnaissance en cours.",
            (OcrStage.Recognizing, _) => "Recognizing text.",
            (_, "ar") => "أُضيفت الصفحة.",
            (_, "fr") => "Page ajoutée.",
            _ => "Page added."
        };
        var file = TotalFiles <= 0 ? "" : lang switch
        {
            "ar" => $"الملف {FileNumber} من {TotalFiles}. ",
            "fr" => $"Fichier {FileNumber} sur {TotalFiles}. ",
            _ => $"File {FileNumber} of {TotalFiles}. "
        };
        if (TotalPages <= 0) return file + stage;
        var pattern = lang switch
        {
            "ar" => "الصفحة {0} من {1}. اكتمل {2:0.#} بالمئة. {3}",
            "fr" => "Page {0} sur {1}. {2:0.#} pour cent terminés. {3}",
            _ => "Page {0} of {1}. {2:0.#} percent complete. {3}"
        };
        return file + string.Format(culture, pattern, Page, TotalPages, Percent, stage);
    }

    internal static string Chunk(string text, int page, int total, bool headings, string? language)
    {
        var prefix = page == 1 ? "" : Environment.NewLine + Environment.NewLine;
        if (!headings) return prefix + (page == 1 ? "" : $"--- Page {page} ---{Environment.NewLine}") + text.Trim();
        var heading = ExperienceMessages.Language(language) switch
        {
            "ar" => $"الصفحة {page} من {total}",
            "fr" => $"Page {page} sur {total}",
            _ => $"Page {page} of {total}"
        };
        return prefix + heading + Environment.NewLine + text.Trim();
    }
}

// The caller supplies monotonic elapsed time, making cadence independent of
// page speed. Page completion always updates text, even when speech is throttled.
internal sealed class OcrAnnouncementCadence
{
    private TimeSpan last;
    internal bool Due(TimeSpan elapsed, int seconds, bool pageCompleted)
    {
        seconds = AppSettings.NormalizeProgressInterval(seconds);
        if (seconds == 0) return pageCompleted;
        if (elapsed - last < TimeSpan.FromSeconds(seconds)) return false;
        last = elapsed;
        return true;
    }
}

internal sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
