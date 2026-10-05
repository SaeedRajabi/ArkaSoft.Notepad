import io

# ---------- XAML: heading buttons -> Segoe UI (composite font blanks them) ----------
p = "MainWindow.xaml"
s = io.open(p, encoding="utf-8").read()
s = s.replace('FontFamily="{DynamicResource F.Ui}"\n                        FontSize="12" FontWeight="Bold" Margin="2,0"\n                        ToolTip="{DynamicResource T.Heading1}"',
              'FontFamily="Segoe UI"\n                        FontSize="12" FontWeight="Bold" Margin="2,0"\n                        ToolTip="{DynamicResource T.Heading1}"')
s = s.replace('FontFamily="{DynamicResource F.Ui}"\n                        FontSize="12" FontWeight="Bold" Margin="2,0"\n                        ToolTip="{DynamicResource T.Heading2}"',
              'FontFamily="Segoe UI"\n                        FontSize="12" FontWeight="Bold" Margin="2,0"\n                        ToolTip="{DynamicResource T.Heading2}"')
s = s.replace('FontFamily="{DynamicResource F.Ui}"\n                        FontSize="12" FontWeight="SemiBold" Margin="2,0"\n                        ToolTip="{DynamicResource T.Heading3}"',
              'FontFamily="Segoe UI"\n                        FontSize="12" FontWeight="SemiBold" Margin="2,0"\n                        ToolTip="{DynamicResource T.Heading3}"')
s = s.replace('FontFamily="{DynamicResource F.Ui}"\n                        FontSize="12" Margin="2,0"\n                        ToolTip="{DynamicResource T.NormalText}"',
              'FontFamily="Segoe UI"\n                        FontSize="12" Margin="2,0"\n                        ToolTip="{DynamicResource T.NormalText}"')
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("xaml heading fonts fixed")

# ---------- MainWindow.cs ----------
p2 = "MainWindow.xaml.cs"
s2 = io.open(p2, encoding="utf-8").read()

# 1) default values in combos at startup
old = '''        FontSizeBox.ItemsSource = new[]
            { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };'''
new = '''        FontSizeBox.ItemsSource = new[]
            { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };
        _syncingFormatBar = true;
        var defaultFamily = Fonts.SystemFontFamilies.FirstOrDefault(f =>
            string.Equals(f.Source, _settings.EnglishEditorFont, StringComparison.OrdinalIgnoreCase));
        FontFamilyBox.SelectedItem = defaultFamily
            ?? Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Segoe UI");
        FontSizeBox.SelectedItem = "14";
        _syncingFormatBar = false;'''
assert old in s2, "combo defaults"
s2 = s2.replace(old, new)

# 2) toolbar state: keep last value when selection has none
old2 = '''            var family = GetSelectionValue<FontFamily>(tab.Editor, TextElement.FontFamilyProperty);
            FontFamilyBox.SelectedItem = family is null ? null
                : Fonts.SystemFontFamilies.FirstOrDefault(f =>
                      string.Equals(f.Source, family.Source, StringComparison.OrdinalIgnoreCase));

            var size = GetSelectionValue<double>(tab.Editor, TextElement.FontSizeProperty);
            FontSizeBox.Text = size > 0 ? size.ToString("0.#") : string.Empty;'''
new2 = '''            var family = GetSelectionValue<FontFamily>(tab.Editor, TextElement.FontFamilyProperty);
            if (family is not null)
            {
                // no explicit font in the selection: keep the last shown value
                var match = Fonts.SystemFontFamilies.FirstOrDefault(f =>
                    string.Equals(f.Source, family.Source, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    FontFamilyBox.SelectedItem = match;
            }

            var size = GetSelectionValue<double>(tab.Editor, TextElement.FontSizeProperty);
            if (size > 0)
                FontSizeBox.Text = size.ToString("0.#");'''
assert old2 in s2, "toolbar state"
s2 = s2.replace(old2, new2)

# 3) image size cap by window width + own paragraph insertion
old3 = '''    private void InsertImageFromFile(string path)
    {
        if (_active is null)
            return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 800;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = 640,
                Margin = new Thickness(2),
                Tag = path
            };
            var container = new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
            var caret = _active.Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var paragraph = caret.Paragraph ?? new Paragraph();
            if (caret.Paragraph is null)
            {
                _active.Editor.Document.Blocks.Add(paragraph);
                caret = paragraph.ContentStart;
            }
            paragraph.Inlines.Add(container);
            _active.Editor.CaretPosition = paragraph.ContentEnd;
            MarkActiveDirty();
            FocusActiveEditor();
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenError", path, ex.Message));
        }
    }'''
new3 = '''    /// <summary>Max image display size follows the editor width:
    /// wide window 600px, small 300px, very small 200px.</summary>
    private double ImageDisplayCap()
    {
        var width = _active?.Editor.ActualWidth ?? 0;
        if (width >= 1100) return 600;
        if (width >= 700) return 300;
        return 200;
    }

    /// <summary>Inserts an inline element in its OWN paragraph so a single line
    /// number covers the whole image; an empty paragraph follows for continued
    /// typing. Content taller than the viewport scrolls with the document.</summary>
    private void InsertStandaloneInline(UIElement element)
    {
        if (_active is null)
            return;
        var doc = _active.Editor.Document;
        var caret = _active.Editor.CaretPosition;
        var current = caret.Paragraph;

        var mediaParagraph = new Paragraph { Margin = new Thickness(0) };
        mediaParagraph.Inlines.Add(new InlineUIContainer(element)
        {
            BaselineAlignment = BaselineAlignment.Center
        });

        var direction = current?.FlowDirection ?? _active.TextLayout.InputDirection;
        BilingualText.ApplyDirection(mediaParagraph, direction,
            _active.TextLayout.PersianFont, _active.TextLayout.EnglishFont);

        if (current is not null)
        {
            doc.Blocks.InsertAfter(current, mediaParagraph);
            var spacer = new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0) };
            BilingualText.ApplyDirection(spacer, direction,
                _active.TextLayout.PersianFont, _active.TextLayout.EnglishFont);
            doc.Blocks.InsertAfter(mediaParagraph, spacer);
            _active.Editor.CaretPosition = spacer.ContentStart;
        }
        else
        {
            doc.Blocks.Add(mediaParagraph);
            _active.Editor.CaretPosition = mediaParagraph.ContentEnd;
        }

        MarkActiveDirty();
        FocusActiveEditor();
    }

    private void InsertImageFromFile(string path)
    {
        if (_active is null)
            return;
        try
        {
            var cap = ImageDisplayCap();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1200;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = cap,
                MaxHeight = cap,
                Margin = new Thickness(2),
                Tag = path
            };
            InsertStandaloneInline(image);
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenError", path, ex.Message));
        }
    }'''
assert old3 in s2, "image insert"
s2 = s2.replace(old3, new3)

# 4) video insert -> standalone paragraph too
old4 = '''            var panel = new StackPanel { Tag = path, Margin = new Thickness(2) };
            panel.Children.Add(media);
            panel.Children.Add(playButton);

            var container = new InlineUIContainer(panel) { BaselineAlignment = BaselineAlignment.Center };
            var caret = _active.Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var paragraph = caret.Paragraph ?? new Paragraph();
            if (caret.Paragraph is null)
            {
                _active.Editor.Document.Blocks.Add(paragraph);
                caret = paragraph.ContentStart;
            }
            paragraph.Inlines.Add(container);
            _active.Editor.CaretPosition = paragraph.ContentEnd;
            MarkActiveDirty();
            FocusActiveEditor();'''
new4 = '''            var panel = new StackPanel { Tag = path, Margin = new Thickness(2) };
            panel.Children.Add(media);
            panel.Children.Add(playButton);

            InsertStandaloneInline(panel);'''
assert old4 in s2, "video insert"
s2 = s2.replace(old4, new4)

io.open(p2, "w", encoding="utf-8", newline="").write(s2)
print("MainWindow patched")
