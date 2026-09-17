using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ArkaSoft.Notepad.UI.Helpers;

/// <summary>Presentation only: never reorder text or insert bidi control characters.</summary>
public sealed class BilingualText
{
    private sealed class ParagraphState
    {
        public bool WasEmpty { get; set; }
        public bool Manual { get; set; }
    }

    private readonly ConditionalWeakTable<Paragraph, ParagraphState> _paragraphs = new();
    private static readonly Style LeftParagraph = CreateParagraphStyle(FlowDirection.LeftToRight);
    private static readonly Style RightParagraph = CreateParagraphStyle(FlowDirection.RightToLeft);
    private readonly RichTextBox _editor;
    private bool _updating;

    public BilingualText(RichTextBox editor) => _editor = editor;

    public FlowDirection InputDirection { get; set; } = FlowDirection.LeftToRight;

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
                var known = _paragraphs.TryGetValue(paragraph, out var state);
                state ??= _paragraphs.GetOrCreateValue(paragraph);
                if (!state.Manual && (!known || empty || state.WasEmpty))
                    SetDirection(paragraph, empty ? InputDirection : DetectDirection(text) ?? InputDirection);
                state.WasEmpty = empty;
                if (empty)
                    state.Manual = false;
            }
        }
        finally { _updating = false; }
    }

    public void SetSelectionDirection(FlowDirection direction)
    {
        foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.ContentEnd.CompareTo(_editor.Selection.Start) < 0 ||
                paragraph.ContentStart.CompareTo(_editor.Selection.End) > 0)
                continue;
            SetDirection(paragraph, direction);
            _paragraphs.GetOrCreateValue(paragraph).Manual = true;
        }
    }

    private static void SetDirection(Paragraph paragraph, FlowDirection direction)
    {
        // WPF copies effective formatting into local values on Enter/paste.
        // Remove those snapshots so this paragraph's presentation style wins.
        if (paragraph.ReadLocalValue(Block.FlowDirectionProperty) != DependencyProperty.UnsetValue)
            paragraph.ClearValue(Block.FlowDirectionProperty);
        if (paragraph.ReadLocalValue(Block.TextAlignmentProperty) != DependencyProperty.UnsetValue)
            paragraph.ClearValue(Block.TextAlignmentProperty);
        if (paragraph.ReadLocalValue(Block.MarginProperty) != DependencyProperty.UnsetValue)
            paragraph.ClearValue(Block.MarginProperty);
        var style = direction == FlowDirection.RightToLeft ? RightParagraph : LeftParagraph;
        if (!ReferenceEquals(paragraph.Style, style))
            paragraph.Style = style;
    }

    private static Style CreateParagraphStyle(FlowDirection direction)
    {
        // Style values affect layout without creating formatting undo records.
        var style = new Style(typeof(Paragraph));
        style.Setters.Add(new Setter(Block.FlowDirectionProperty, direction));
        style.Setters.Add(new Setter(Block.TextAlignmentProperty,
            direction == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left));
        style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        style.Seal();
        return style;
    }

    public void UpdatePageWidth(bool wrap)
    {
        var doc = _editor.Document;
        if (wrap)
        {
            if (!double.IsNaN(doc.PageWidth))
                doc.PageWidth = double.NaN;
            return;
        }

        // A fixed 100000-DIP page puts right-aligned text far outside the viewport.
        // Use the viewport for short lines and expand only for real unwrapped content.
        var viewport = _editor.ViewportWidth > 0 ? _editor.ViewportWidth : _editor.ActualWidth;
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
    }
}
