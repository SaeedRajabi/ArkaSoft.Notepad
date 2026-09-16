using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ArkaSoft.Notepad.UI.Helpers;

namespace ArkaSoft.Notepad.UI.Services;

/// <summary>
/// Paginates plain text for printing: applies char-level wrapping to the
/// printable width and lays lines out sequentially with the page margins.
/// </summary>
public sealed class TextPrintPaginator : DocumentPaginator
{
    // 1 inch = 96 DIP in WPF; 1 mm = 96/25.4
    private const double MmToDip = 96.0 / 25.4;

    private readonly string[] _lines;
    private readonly FontFamily _fontFamily;
    private readonly double _fontSize;
    private readonly Size _pageSize;
    private readonly Thickness _margins;
    private readonly Brush _foreground;

    public TextPrintPaginator(string text, FontFamily fontFamily, double fontSize,
        Size pageSize, Thickness margins, Brush foreground)
    {
        _fontFamily = fontFamily;
        _fontSize = fontSize;
        _pageSize = pageSize;
        _margins = margins;
        _foreground = foreground;
        _lines = SplitIntoVisualLines(text);
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => (int)Math.Ceiling(_lines.Length / (double)LinesPerPage);

    public override Size PageSize
    {
        get => _pageSize;
        set => throw new NotSupportedException();
    }

    public override IDocumentPaginatorSource Source => null!;

    private double LineHeight => _fontSize * 1.35;

    private double ContentWidth => _pageSize.Width - _margins.Left - _margins.Right;

    private int LinesPerPage => Math.Max(1,
        (int)((_pageSize.Height - _margins.Top - _margins.Bottom) / LineHeight));

    private string[] SplitIntoVisualLines(string text)
    {
        var logical = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var visual = new List<string>(logical.Length);
        var typeface = new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        foreach (var logicalLine in logical)
        {
            if (logicalLine.Length == 0)
            {
                visual.Add(string.Empty);
                continue;
            }

            var remaining = logicalLine;
            while (remaining.Length > 0)
            {
                var formatted = new FormattedText(
                    remaining,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    _fontSize,
                    _foreground,
                    pixelsPerDip: 1.0);

                if (formatted.WidthIncludingTrailingWhitespace <= ContentWidth)
                {
                    visual.Add(remaining);
                    break;
                }

                int fit = (int)Math.Max(1, (long)Math.Floor(
                    (double)remaining.Length * ContentWidth /
                    Math.Max(1, formatted.WidthIncludingTrailingWhitespace)));
                while (fit > 1)
                {
                    var probe = new FormattedText(
                        remaining[..fit],
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        _fontSize,
                        _foreground,
                        pixelsPerDip: 1.0);
                    if (probe.WidthIncludingTrailingWhitespace <= ContentWidth)
                        break;
                    fit--;
                }

                visual.Add(remaining[..fit]);
                remaining = remaining[fit..];
            }
        }
        return visual.ToArray();
    }

    public override DocumentPage GetPage(int pageNumber)
    {
        int linesPerPage = LinesPerPage;
        int first = pageNumber * linesPerPage;
        if (first >= _lines.Length)
            return DocumentPage.Missing;

        var typeface = new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            int last = Math.Min(_lines.Length, first + linesPerPage);
            for (int i = first; i < last; i++)
            {
                double y = _margins.Top + (i - first) * LineHeight + _fontSize * 0.25;
                if (_lines[i].Length == 0)
                    continue;
                var formatted = new FormattedText(
                    _lines[i],
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    _fontSize,
                    _foreground,
                    pixelsPerDip: 1.0);
                dc.DrawText(formatted, new Point(_margins.Left, y));
            }
        }
        return new DocumentPage(visual, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
    }

    public static Size GetPaperSize(string paperName, bool landscape)
    {
        // sizes in mm
        var (widthMm, heightMm) = paperName switch
        {
            "A5" => (148.0, 210.0),
            "B5" => (176.0, 250.0),
            "Letter" => (215.9, 279.4),
            "Legal" => (215.9, 355.6),
            "Executive" => (184.2, 266.7),
            _ => (210.0, 297.0) // A4
        };
        double w = widthMm * MmToDip;
        double h = heightMm * MmToDip;
        return landscape ? new Size(h, w) : new Size(w, h);
    }

    public static Thickness MarginsToDip(PrintSettings settings) => new(
        settings.MarginLeftMm * MmToDip,
        settings.MarginTopMm * MmToDip,
        settings.MarginRightMm * MmToDip,
        settings.MarginBottomMm * MmToDip);
}

public static class PrintService
{
    public static void Print(Window owner, RichTextBox editor, PrintSettings settings)
    {
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            var paper = TextPrintPaginator.GetPaperSize(settings.PaperName, settings.Landscape);
            // the printer driver dictates the physical page; keep configured orientation/margins
            var paginator = new TextPrintPaginator(
                editor.GetText(),
                editor.FontFamily,
                editor.FontSize,
                dialog.PrintableAreaWidth > 0 ? new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight) : paper,
                TextPrintPaginator.MarginsToDip(settings),
                Brushes.Black);

            dialog.PrintDocument(paginator, "Notepad Document");
        }
        catch (Exception ex)
        {
            Dialogs.MessageDialog.ShowInfo(owner, "Notepad",
                $"Printing failed:{Environment.NewLine}{ex.Message}");
        }
    }
}
