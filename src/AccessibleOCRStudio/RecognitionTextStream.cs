using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;

// Only completed engine results enter the editor. Never animate invented text
// or move the reader's caret/scroll position to the newly appended page.
internal sealed class RecognitionTextStream(TextBox editor, string languages)
{
    public bool HasChunks { get; private set; }
    public bool HasText { get; private set; }
    private bool directionKnown;

    public void Append(OcrProgress update)
    {
        if (update.TextChunk is null) return;
        if (!HasChunks) editor.Clear();
        HasChunks = true;
        HasText |= !string.IsNullOrWhiteSpace(update.PageText);
        if (!directionKnown && update.PageText?.FirstOrDefault(char.IsLetter) is char first && first != default)
        {
            var rtl = first >= '\u0600' && first <= '\u06ff';
            editor.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            editor.Language = XmlLanguage.GetLanguage(rtl ? "ar" : languages == "fra" ? "fr" : "en");
            directionKnown = true;
        }
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        var vertical = editor.VerticalOffset;
        var horizontal = editor.HorizontalOffset;
        editor.AppendText(update.TextChunk);
        editor.Select(start, length);
        editor.ScrollToVerticalOffset(vertical);
        editor.ScrollToHorizontalOffset(horizontal);
    }
}
