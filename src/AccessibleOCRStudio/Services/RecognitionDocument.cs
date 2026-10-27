namespace AccessibleOCRStudio.Services;

public sealed record OcrWord(string Text, double Confidence, int Page, int Line, int Left, int Top, int Width, int Height);
public sealed record DocumentBlock(string Kind, string Text, int Page, bool RightToLeft = false);

public sealed class RecognitionDocument
{
    public string PlainText { get; set; } = string.Empty;
    public int ContentStart { get; set; }
    public List<OcrWord> Words { get; } = new();
    public List<DocumentBlock> Blocks { get; } = new();
    public double AverageConfidence => Words.Count == 0 ? 0 : Words.Average(w => w.Confidence);
    public IEnumerable<OcrWord> LowConfidenceWords(double threshold = 70) => Words.Where(w => w.Confidence >= 0 && w.Confidence < threshold);
    // Match all OCR words in reading order before filtering, so repeated words retain their own location.
    public IReadOnlyList<(OcrWord Word, int Start)> ReviewLocations(string currentText, double threshold)
    {
        if (currentText.Replace("\r\n", "\n") != PlainText.Replace("\r\n", "\n"))
            return Array.Empty<(OcrWord, int)>();
        var result = new List<(OcrWord, int)>();
        var cursor = ContentStart;
        foreach (var word in Words)
        {
            if (string.IsNullOrEmpty(word.Text)) continue;
            var start = currentText.IndexOf(word.Text, cursor, StringComparison.Ordinal);
            if (start < 0) return Array.Empty<(OcrWord, int)>();
            cursor = start + word.Text.Length;
            if (word.Confidence >= 0 && word.Confidence < threshold) result.Add((word, start));
        }
        return result;
    }
}
