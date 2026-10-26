using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;

// Completed OCR pages append to the editable document. Page anchors follow
// user edits so navigation never relies on unchanged page-heading text.
internal sealed class RecognitionTextStream : IDisposable
{
    private sealed class PageAnchor(int number, int start, string heading)
    { internal int Number = number; internal int Start = start; internal string Heading = heading; }
    private readonly TextBox editor;
    private readonly string languages;
    private readonly Func<bool>? followNewPages;
    private readonly string initialText;
    private readonly List<PageAnchor> pages = new();
    private bool appending;
    private bool directionKnown;
    private long lastUserEdit = Environment.TickCount64 - 3000;
    internal event Action? Updated;
    public bool HasChunks => pages.Count > 0;
    public bool HasText { get; private set; }
    internal int LatestPageStart => pages.Count == 0 ? 0 : pages[^1].Start;
    internal int LatestPageNumber => pages.Count == 0 ? 0 : pages[^1].Number;
    internal int TotalPages { get; private set; }
    internal int AvailablePages => pages.Count;
    internal int CurrentPageNumber => pages.Count == 0 ? 0 : pages[Math.Max(0, CurrentIndex)].Number;
    private int CurrentIndex => pages.FindLastIndex(page => page.Start <= editor.CaretIndex);

    internal RecognitionTextStream(TextBox editor, string languages, Func<bool>? followNewPages = null)
    {
        this.editor = editor; this.languages = languages; this.followNewPages = followNewPages;
        initialText = editor.Text;
        editor.TextChanged += Edited;
    }

    private void Edited(object sender, TextChangedEventArgs e)
    {
        if (appending) return;
        lastUserEdit = Environment.TickCount64;
        var crossedBoundary = e.Changes.Any(change => change.RemovedLength > 0 &&
            pages.Any(page => page.Start >= change.Offset && page.Start < change.Offset + change.RemovedLength));
        foreach (var change in e.Changes.OrderByDescending(change => change.Offset))
            foreach (var page in pages)
                if (change.Offset < page.Start)
                    page.Start = Math.Max(change.Offset, page.Start - change.RemovedLength) + change.AddedLength;
        var collapsed = pages.Where((page, index) => index > 0 && page.Start == pages[index - 1].Start).Any();
        if (crossedBoundary || collapsed || e.Changes.Any(change => change.AddedLength > 256 || change.RemovedLength > 256)) ResyncHeadings();
        Updated?.Invoke();
    }

    private void ResyncHeadings()
    {
        // A whole-document cut/paste or Undo can restore several headers at once.
        // Scan forwards, rather than rescanning the full document for each page.
        var text = editor.Text;
        var searchFrom = 0;
        foreach (var page in pages)
        {
            var found = text.IndexOf(page.Heading, searchFrom, StringComparison.Ordinal);
            while (found >= 0 && (found > 0 && text[found - 1] != '\n' ||
                found + page.Heading.Length < text.Length && text[found + page.Heading.Length] is not '\r' and not '\n'))
                found = text.IndexOf(page.Heading, found + 1, StringComparison.Ordinal);
            if (found < 0) continue;
            page.Start = found;
            searchFrom = found + page.Heading.Length;
        }
    }

    private static bool ArabicLetter(char c) => char.IsLetter(c) && (c is >= '\u0600' and <= '\u06ff' or >= '\u0750' and <= '\u077f' or >= '\u08a0' and <= '\u08ff' or >= '\ufb50' and <= '\ufdff' or >= '\ufe70' and <= '\ufeff');

    public void Append(OcrProgress update)
    {
        if (update.TextChunk is null) return;
        var clearInitial = !HasChunks && editor.Text == initialText;
        var separator = !HasChunks && !clearInitial && editor.Text.Length > 0 ? Environment.NewLine + Environment.NewLine : "";
        var start = clearInitial ? 0 : editor.SelectionStart;
        var length = clearInitial ? 0 : editor.SelectionLength;
        var vertical = editor.VerticalOffset;
        var horizontal = editor.HorizontalOffset;
        var insertionStart = (clearInitial ? 0 : editor.Text.Length) + separator.Length;
        appending = true;
        editor.BeginChange();
        try
        {
            if (clearInitial) editor.Clear();
            HasText |= !string.IsNullOrWhiteSpace(update.PageText);
            if (!directionKnown && update.PageText?.FirstOrDefault(char.IsLetter) is char first && first != default)
            {
                var rtl = languages == "ara" || update.PageText.Count(ArabicLetter) > update.PageText.Count(c => char.IsLetter(c) && !ArabicLetter(c));
                editor.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                editor.Language = XmlLanguage.GetLanguage(rtl ? "ar" : languages == "fra" ? "fr" : "en");
                directionKnown = true;
            }
            editor.AppendText(separator + update.TextChunk);
            var headingText = update.TextChunk.TrimStart('\r', '\n');
            var headingEnd = headingText.IndexOfAny(new[] { '\r', '\n' });
            pages.Add(new(update.Page, insertionStart + update.TextChunk.Length - headingText.Length, headingEnd < 0 ? headingText : headingText[..headingEnd]));
            TotalPages = update.TotalPages;
        }
        finally { editor.EndChange(); appending = false; }
        // TextBox defers its Text property while a change block is open. Move
        // only after EndChange so the new page's offset is not clamped to old text.
        if (followNewPages?.Invoke() == true && length == 0 && Environment.TickCount64 - lastUserEdit >= 2000) GoToLatestPage();
        else
        {
            if (editor.SelectionStart != start || editor.SelectionLength != length) editor.Select(start, length);
            if (editor.VerticalOffset != vertical) editor.ScrollToVerticalOffset(vertical);
            if (editor.HorizontalOffset != horizontal) editor.ScrollToHorizontalOffset(horizontal);
        }
        Updated?.Invoke();
    }

    internal bool GoToLatestPage() => GoToPage(pages.Count - 1);
    internal bool MovePage(int direction) => GoToPage(CurrentIndex + direction);
    private bool GoToPage(int index)
    {
        if (index < 0 || index >= pages.Count) return false;
        editor.Select(Math.Min(pages[index].Start, editor.Text.Length), 0);
        editor.ScrollToLine(editor.GetLineIndexFromCharacterIndex(editor.CaretIndex));
        Updated?.Invoke();
        return true;
    }
    public void Dispose() => editor.TextChanged -= Edited;
}
