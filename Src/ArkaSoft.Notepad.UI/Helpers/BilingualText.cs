using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ArkaSoft.Notepad.UI.Helpers;

/// <summary>
/// How paragraph directions are decided for one editor.
/// </summary>
public enum TextDirectionMode
{
    /// <summary>Auto: each paragraph follows its content; empty paragraphs
    /// follow the active keyboard language. Persian sticks right, English
    /// sticks left, and switching language mid-sentence changes nothing.</summary>
    Normal,

    /// <summary>Everything is right-to-left, regardless of content.</summary>
    Rtl,

    /// <summary>Everything is left-to-right, regardless of content.</summary>
    Ltr
}

/// <summary>Presentation only: never reorder text or insert bidi control characters.</summary>
public sealed class BilingualText
{
    private readonly RichTextBox _editor;
    private bool _updating;

    public BilingualText(RichTextBox editor) => _editor = editor;

    /// <summary>The direction an EMPTY paragraph takes in Normal mode
    /// (driven by the active keyboard language).</summary>
    public FlowDirection InputDirection { get; set; } = FlowDirection.LeftToRight;

    public TextDirectionMode Mode { get; set; } = TextDirectionMode.Normal;

    public bool IsPinned => Mode != TextDirectionMode.Normal;

    public static FlowDirection DirectionFor(CultureInfo culture)
        => culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static FlowDirection? DetectDirection(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            // Digits, punctuation, combining marks and ZWNJ do not establish a base direction.
            if (!System.Text.Rune.IsLetter(rune))
                continue;
            return rune.Value is >= 0x590 and <= 0x8FF or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
        return null;
    }

    /// <summary>Font used by right-to-left (Persian) paragraphs; set from
    /// TypographyService. Null leaves the document font untouched.</summary>
    public FontFamily? PersianFont { get; set; }

    /// <summary>Font used by left-to-right (English) paragraphs.</summary>
    public FontFamily? EnglishFont { get; set; }

    public void Refresh()
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>())
            {
                var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                var empty = string.IsNullOrWhiteSpace(text);

                FlowDirection direction = Mode switch
                {
                    TextDirectionMode.Rtl => FlowDirection.RightToLeft,
                    TextDirectionMode.Ltr => FlowDirection.LeftToRight,
                    _ => empty ? InputDirection : DetectDirection(text) ?? InputDirection
                };

                ApplyDirection(paragraph, direction, PersianFont, EnglishFont);
            }
        }
        finally { _updating = false; }
    }

    /// <summary>Pins the whole document to one direction (RTL/LTR modes).</summary>
    public void SetDocumentDirection(FlowDirection direction)
    {
        Mode = direction == FlowDirection.RightToLeft ? TextDirectionMode.Rtl : TextDirectionMode.Ltr;
        InputDirection = direction;
    }

    /// <summary>Returns to content-driven direction (the Normal mode).</summary>
    public void ResetToNormal()
    {
        Mode = TextDirectionMode.Normal;
    }

    /// <summary>
    /// Gives the paragraph at the caret the given direction, but ONLY while it
    /// is empty — so a paragraph picks up the typing language at its start and
    /// then KEEPS that direction when the language switches mid-sentence. This
    /// is what prevents the left/right flip and font jump inside a sentence.
    /// In forced RTL/LTR modes this is a no-op; Refresh pins every paragraph.
    /// </summary>
    public void ApplyInputDirectionToEmptyCaretParagraph(FlowDirection direction)
    {
        if (Mode != TextDirectionMode.Normal)
            return;

        var paragraph = _editor.CaretPosition.Paragraph;
        if (paragraph is null)
            return;

        var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        if (!string.IsNullOrWhiteSpace(text))
            return;

        SetDirection(paragraph, direction);

        // ContentStart is the logical beginning; in an RTL paragraph it is
        // rendered at the right edge, and in an LTR paragraph at the left edge.
        _editor.Selection.Select(paragraph.ContentStart, paragraph.ContentStart);
        _editor.CaretPosition = paragraph.ContentStart;
    }

    /// <summary>
    /// TextAlignment is flow-relative in WPF: on a paragraph whose FlowDirection
    /// is RightToLeft, the start edge ("Left") is the visual RIGHT edge, and
    /// "Right" would push the text to the visual left. Aligning both directions
    /// to their start edge is what makes Persian stick right and English left.
    /// </summary>
    private static TextAlignment AlignmentFor(FlowDirection direction)
        => TextAlignment.Left;

    private void SetDirection(Paragraph paragraph, FlowDirection direction)
        => ApplyDirection(paragraph, direction, PersianFont, EnglishFont);

    /// <summary>
    /// Applies one direction to a paragraph. Local values are required: a
    /// FlowDocument can retain a previous inherited LTR layout even after its
    /// paragraph style changes. TextAlignment is flow-relative — the start
    /// edge ("Left") is the visual RIGHT edge on RTL paragraphs. Static so
    /// batched (chunked) direction changes can reuse it.
    /// </summary>
    public static void ApplyDirection(Paragraph paragraph, FlowDirection direction,
        FontFamily? persianFont, FontFamily? englishFont)
    {
        paragraph.FlowDirection = direction;
        paragraph.TextAlignment = AlignmentFor(direction);
        paragraph.Margin = new Thickness(0);

        // A Persian paragraph renders entirely in the Persian font (including
        // spaces and punctuation), an English paragraph entirely in the English
        // one — that keeps word spacing consistent inside every line.
        if (direction == FlowDirection.RightToLeft && persianFont is not null)
            paragraph.FontFamily = persianFont;
        else if (direction == FlowDirection.LeftToRight && englishFont is not null)
            paragraph.FontFamily = englishFont;
    }

    public void UpdatePageWidth(bool wrap)
    {
        var doc = _editor.Document;
        var viewport = _editor.ViewportWidth > 0 ? _editor.ViewportWidth : _editor.ActualWidth;
        if (wrap)
        {
            // FlowDocument otherwise uses its default narrow column. Paragraph
            // alignment is then correct only inside that column, which makes a
            // visually RTL line appear on the left of a wide editor.
            if (viewport > 0 && (double.IsNaN(doc.ColumnWidth) || Math.Abs(doc.ColumnWidth - viewport) > 0.5))
                doc.ColumnWidth = viewport;
            if (!double.IsNaN(doc.PageWidth))
                doc.PageWidth = double.NaN;
            return;
        }

        // A fixed 100000-DIP page puts right-aligned text far outside the viewport.
        // Use the viewport for short lines and expand only for real unwrapped content.
        var width = Math.Max(100, viewport);
        var typeface = new Typeface(doc.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        foreach (var paragraph in doc.Blocks.OfType<Paragraph>())
        {
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, paragraph.FlowDirection,
                typeface, doc.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(_editor).PixelsPerDip);
            width = Math.Max(width, formatted.WidthIncludingTrailingWhitespace + doc.PagePadding.Left + doc.PagePadding.Right + 24);
        }
        if (double.IsNaN(doc.PageWidth) || Math.Abs(doc.PageWidth - width) > 0.5)
            doc.PageWidth = width;
        // Without this the no-wrap layout also flows into the default ~280px
        // column and right-aligned Persian lines stop far from the right edge.
        var columnWidth = Math.Max(1, width - doc.PagePadding.Left - doc.PagePadding.Right);
        if (double.IsNaN(doc.ColumnWidth) || Math.Abs(doc.ColumnWidth - columnWidth) > 0.5)
            doc.ColumnWidth = columnWidth;
    }
}
