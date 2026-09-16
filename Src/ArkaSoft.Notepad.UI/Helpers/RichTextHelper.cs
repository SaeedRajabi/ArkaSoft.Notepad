using System.Windows.Controls;
using System.Windows.Documents;

namespace ArkaSoft.Notepad.UI.Helpers;

/// <summary>
/// Bridges between the plain-text world (indexes over the full document text,
/// where every paragraph break counts as "\r\n") and TextPointers inside a
/// RichTextBox's FlowDocument.
/// </summary>
public static class RichTextHelper
{
    /// <summary>Full plain text of the document; paragraphs joined with "\r\n".</summary>
    public static string GetText(this RichTextBox rtb)
        => new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;

    public static void SetText(this RichTextBox rtb, string text)
    {
        var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        range.Text = text;
        rtb.CaretPosition = rtb.Document.ContentStart;
    }

    /// <summary>Char index of an arbitrary TextPointer within the full document text
    /// (paragraph breaks counted as 2 chars, matching TextRange.Text).</summary>
    public static int IndexOf(FlowDocument doc, TextPointer position)
    {
        int index = 0;
        var tp = doc.ContentStart;
        while (tp is not null && tp.CompareTo(position) < 0)
        {
            switch (tp.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var next = tp.GetNextContextPosition(LogicalDirection.Forward);
                    if (next is null || next.CompareTo(position) > 0)
                        return index + Math.Max(0, tp.GetOffsetToPosition(position));
                    index += tp.GetTextRunLength(LogicalDirection.Forward);
                    tp = next;
                    break;
                case TextPointerContext.ElementEnd when tp.Parent is Paragraph:
                    var after = tp.GetNextContextPosition(LogicalDirection.Forward);
                    if (after is null || after.CompareTo(position) > 0)
                        return index;
                    index += 2;
                    tp = after;
                    break;
                default:
                    tp = tp.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }
        return index;
    }

    /// <summary>TextPointer at a char index within the full document text
    /// (paragraph breaks counted as 2 chars, matching TextRange.Text).</summary>
    public static TextPointer PointerAtIndex(FlowDocument doc, int index)
    {
        var tp = doc.ContentStart;
        int consumed = 0;
        while (tp is not null)
        {
            switch (tp.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    int runLen = tp.GetTextRunLength(LogicalDirection.Forward);
                    if (consumed + runLen >= index)
                        return GetPositionInRun(tp, index - consumed);
                    consumed += runLen;
                    tp = tp.GetNextContextPosition(LogicalDirection.Forward);
                    break;
                case TextPointerContext.ElementEnd when tp.Parent is Paragraph:
                    if (consumed + 2 > index)
                        return tp.GetNextContextPosition(LogicalDirection.Forward) ?? doc.ContentEnd;
                    consumed += 2;
                    tp = tp.GetNextContextPosition(LogicalDirection.Forward);
                    break;
                default:
                    tp = tp.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }
        return doc.ContentEnd;
    }

    private static TextPointer GetPositionInRun(TextPointer tp, int offset)
    {
        var max = tp.GetTextRunLength(LogicalDirection.Forward);
        return tp.GetPositionAtOffset(Math.Clamp(offset, 0, max), LogicalDirection.Forward);
    }

    /// <summary>Line/column for the caret; when word wrap is off every paragraph is one line.</summary>
    public static (int Line, int Column) GetLineColumn(RichTextBox rtb)
    {
        var caret = rtb.CaretPosition;
        var lineStart = caret.GetLineStartPosition(0);
        if (lineStart is null)
            return (1, 1);

        int line = 1;
        var walk = lineStart;
        while (walk.GetLineStartPosition(-1) is { } prev)
        {
            walk = prev;
            line++;
        }

        int column = Math.Max(1, IndexOf(rtb.Document, caret) - IndexOf(rtb.Document, lineStart) + 1);
        return (line, column);
    }

    /// <summary>Total number of text lines (paragraphs) in the document.</summary>
    public static int CountLines(RichTextBox rtb)
    {
        var text = rtb.GetText();
        if (text.Length == 0)
            return 1;
        int count = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                count++;
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
            count--;
        return Math.Max(1, count);
    }

    public static void ScrollCaretIntoView(RichTextBox rtb)
    {
        var rect = rtb.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
        if (rtb.Template.FindName("PART_ContentHost", rtb) is ScrollViewer viewer)
        {
            var topInContent = rect.Top + viewer.VerticalOffset;
            if (topInContent < viewer.VerticalOffset ||
                topInContent > viewer.VerticalOffset + viewer.ViewportHeight - 20)
            {
                viewer.ScrollToVerticalOffset(Math.Max(0, topInContent - viewer.ViewportHeight / 2));
            }
            var right = rect.Right + viewer.HorizontalOffset;
            if (right > viewer.HorizontalOffset + viewer.ViewportWidth - 20)
                viewer.ScrollToHorizontalOffset(right - viewer.ViewportWidth + 40);
        }
    }

    public static int[] FindAll(string text, string term, bool matchCase)
    {
        var indexes = new List<int>();
        if (string.IsNullOrEmpty(term))
            return indexes.ToArray();
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int idx = 0;
        while ((idx = text.IndexOf(term, idx, comparison)) >= 0)
        {
            indexes.Add(idx);
            idx += term.Length;
        }
        return indexes.ToArray();
    }
}
