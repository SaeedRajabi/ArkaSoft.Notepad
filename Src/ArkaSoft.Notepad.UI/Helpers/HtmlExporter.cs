using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArkaSoft.Notepad.UI.Helpers;

/// <summary>
/// Exports a styled FlowDocument (rich editing output) to standalone HTML:
/// headings, bold/italic/underline, per-run colors, background highlights,
/// font family/size, lists, embedded images (data URIs) and videos.
/// </summary>
public static class HtmlExporter
{
    public static string Export(FlowDocument document)
    {
        var body = BlocksToHtml(document.Blocks, document.FontSize);
        return """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>ArkaSoft Notepad</title>
<style>
  body { font-family: Vazir, 'Segoe UI', Tahoma, sans-serif; font-size: 14px;
         color: #1b1b1b; background: #ffffff; padding: 24px; line-height: 1.6; }
  img, video { max-width: 100%; border-radius: 4px; }
  h1 { font-size: 1.9em; } h2 { font-size: 1.5em; } h3 { font-size: 1.2em; }
</style>
</head>
<body>
""" + body + "\n</body>\n</html>\n";
    }

    private static string BlocksToHtml(IEnumerable<Block> blocks, double baseFontSize)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    sb.Append(ParagraphToHtml(paragraph, baseFontSize));
                    break;
                case List list:
                    sb.Append(ListToHtml(list, baseFontSize));
                    break;
                case Section section:
                    sb.Append(BlocksToHtml(section.Blocks, baseFontSize));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ListToHtml(List list, double baseFontSize)
    {
        var ordered = list.MarkerStyle is TextMarkerStyle.Decimal
            or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman
            or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin;

        var sb = new System.Text.StringBuilder();
        sb.Append(ordered ? "<ol>\n" : "<ul>\n");
        foreach (var item in list.ListItems)
        {
            sb.Append("<li>");
            sb.Append(BlocksToHtml(item.Blocks, baseFontSize));
            sb.Append("</li>\n");
        }
        sb.Append(ordered ? "</ol>\n" : "</ul>\n");
        return sb.ToString();
    }

    private static string ParagraphToHtml(Paragraph paragraph, double baseFontSize)
    {
        var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
        if (string.IsNullOrWhiteSpace(text) && paragraph.Inlines.Count == 0)
            return "<br/>\n";

        var tag = paragraph.Tag as string switch
        {
            "H1" => "h1",
            "H2" => "h2",
            "H3" => "h3",
            _ => "p"
        };

        var dir = paragraph.FlowDirection == FlowDirection.RightToLeft ? "rtl" : "ltr";
        var align = paragraph.FlowDirection == FlowDirection.RightToLeft ? "right" : "left";
        if (paragraph.TextAlignment == TextAlignment.Center) align = "center";
        if (paragraph.TextAlignment == TextAlignment.Justify) align = "justify";

        var sb = new System.Text.StringBuilder();
        sb.Append('<').Append(tag)
          .Append($" dir=\"{dir}\" style=\"text-align:{align}\"")
          .Append('>').Append('\n');
        foreach (var inline in paragraph.Inlines)
            sb.Append(InlinesToHtml(inline, baseFontSize));
        sb.Append("</").Append(tag).Append(">\n");
        return sb.ToString();
    }

    private static string InlinesToHtml(Inline inline, double baseFontSize)
    {
        switch (inline)
        {
            case Run run:
                return WrapWithStyle(run, baseFontSize, Escape(run.Text));
            case Span span:
                {
                    var inner = new System.Text.StringBuilder();
                    foreach (var child in span.Inlines)
                        inner.Append(InlinesToHtml(child, baseFontSize));
                    return WrapWithStyle(span, baseFontSize, inner.ToString());
                }
            case InlineUIContainer container:
                return ContainerToHtml(container);
            default:
                return string.Empty;
        }
    }

    private static string WrapWithStyle(TextElement element, double baseFontSize, string innerHtml)
    {
        var styles = new List<string>();

        if (element is Span span)
        {
            if ((FontWeight)(span.GetValue(TextElement.FontWeightProperty) ?? FontWeights.Normal)
                == FontWeights.Bold)
                styles.Add("font-weight:bold");
            if ((FontStyle)(span.GetValue(TextElement.FontStyleProperty) ?? FontStyles.Normal)
                == FontStyles.Italic)
                styles.Add("font-style:italic");
        }
        if (element.GetValue(Inline.TextDecorationsProperty) is TextDecorationCollection deco &&
            deco.Any(d => d.Location == TextDecorationLocation.Underline))
            styles.Add("text-decoration:underline");

        AppendColorStyle(element, TextElement.ForegroundProperty, "color", styles);
        AppendColorStyle(element, TextElement.BackgroundProperty, "background-color", styles);

        if (element.FontSize > 0 && Math.Abs(element.FontSize - baseFontSize) > 0.1)
            styles.Add($"font-size:{element.FontSize:0.#}px");
        if (element.FontFamily is { } family && !string.IsNullOrEmpty(family.Source))
            styles.Add($"font-family:'{Escape(family.Source)}'");

        if (styles.Count == 0 || string.IsNullOrEmpty(innerHtml))
            return innerHtml;
        return $"<span style=\"{string.Join(";", styles)}\">" + innerHtml + "</span>";
    }

    private static void AppendColorStyle(TextElement element, DependencyProperty property,
        string cssProperty, List<string> styles)
    {
        if (element.GetValue(property) is not SolidColorBrush brush)
            return;
        if (brush.Color == Colors.Transparent)
            return;
        styles.Add($"{cssProperty}:#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}".ToLowerInvariant());
    }

    private static string ContainerToHtml(InlineUIContainer container)
    {
        switch (container.Child)
        {
            case Image { Source: BitmapSource source }:
                {
                    try
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        using var ms = new MemoryStream();
                        encoder.Save(ms);
                        var base64 = Convert.ToBase64String(ms.ToArray());
                        return $"<img src=\"data:image/png;base64,{base64}\" alt=\"\"/>";
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            case MediaElement { Source: { } mediaSource }:
                return VideoTag(mediaSource.ToString());
            case StackPanel { Tag: { } tag } when tag is string videoPath:
                return VideoTag(videoPath);
            default:
                return string.Empty;
        }
    }

    private static string VideoTag(string path)
    {
        var src = path.Replace('\\', '/');
        if (!src.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            src = "file:///" + src.TrimStart('/');
        return $"<video controls src=\"{Escape(src)}\"></video>";
    }

    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
