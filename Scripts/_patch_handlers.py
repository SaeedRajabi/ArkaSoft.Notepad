import io

p = "MainWindow.xaml.cs"
s = io.open(p, encoding="utf-8").read()

anchor = "    private void OpenEmojiPanel()"
assert anchor in s

code = """    // ==================== rich text formatting toolbar ====================

    private bool _syncingFormatBar;

    private void MarkActiveDirty()
    {
        if (_active is null)
            return;
        _active.IsDirty = true;
        UpdateWindowTitle();
    }

    private static T? GetSelectionValue<T>(RichTextBox editor, DependencyProperty property)
    {
        var value = editor.Selection.GetPropertyValue(property);
        return value is T typed ? typed : default;
    }

    private void ApplySelectionProperty(DependencyProperty property, object value)
    {
        if (_active is null)
            return;
        _active.Editor.Selection.ApplyPropertyValue(property, value);
        MarkActiveDirty();
        FocusActiveEditor();
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        _active.Editor.EditingCommands.ToggleBold.Execute(null, _active.Editor);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        _active.Editor.EditingCommands.ToggleItalic.Execute(null, _active.Editor);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        _active.Editor.EditingCommands.ToggleUnderline.Execute(null, _active.Editor);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
    }

    private void BulletListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        _active.Editor.EditingCommands.ToggleBullets.Execute(null, _active.Editor);
        MarkActiveDirty();
    }

    private void NumberListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        _active.Editor.EditingCommands.ToggleNumbering.Execute(null, _active.Editor);
        MarkActiveDirty();
    }

    private void ClearFormatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null)
            return;
        var selection = _active.Editor.Selection;
        if (selection.IsEmpty)
            return;
        selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
        selection.ApplyPropertyValue(TextElement.TextDecorationsProperty, null);
        selection.ApplyPropertyValue(TextElement.ForegroundProperty, null);
        selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        selection.ApplyPropertyValue(TextElement.FontSizeProperty, null);
        selection.ApplyPropertyValue(TextElement.FontFamilyProperty, null);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
        FocusActiveEditor();
    }

    private void HeadingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null)
            return;
        var tag = (sender as FrameworkElement)?.Tag as string ?? "Normal";
        double baseSize = _active.Editor.Document.FontSize;
        foreach (var paragraph in SelectedParagraphs())
        {
            switch (tag)
            {
                case "H1":
                    paragraph.FontSize = baseSize * 1.7;
                    paragraph.FontWeight = FontWeights.Bold;
                    paragraph.Tag = "H1";
                    break;
                case "H2":
                    paragraph.FontSize = baseSize * 1.4;
                    paragraph.FontWeight = FontWeights.Bold;
                    paragraph.Tag = "H2";
                    break;
                case "H3":
                    paragraph.FontSize = baseSize * 1.15;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Tag = "H3";
                    break;
                default:
                    paragraph.FontSize = baseSize;
                    paragraph.FontWeight = FontWeights.Normal;
                    paragraph.Tag = null;
                    break;
            }
            // keep the pinned line height proportional to the (larger) heading font
            paragraph.LineHeight = paragraph.FontSize * 1.45;
            paragraph.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;
        }
        MarkActiveDirty();
        FocusActiveEditor();
    }

    private IEnumerable<Paragraph> SelectedParagraphs()
    {
        if (_active is null)
            yield break;
        var selection = _active.Editor.Selection;
        foreach (var paragraph in _active.Editor.Document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.ContentEnd.CompareTo(selection.Start) < 0 ||
                paragraph.ContentStart.CompareTo(selection.End) > 0)
                continue;
            yield return paragraph;
        }
    }

    private void FontFamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFormatBar || FontFamilyBox.SelectedItem is not string family)
            return;
        ApplySelectionProperty(TextElement.FontFamilyProperty, new FontFamily(family));
    }

    private void FontFamilyBox_DropDownClosed(object sender, EventArgs e)
    {
        if (!_syncingFormatBar && FontFamilyBox.SelectedItem is string family)
        {
            ApplySelectionProperty(TextElement.FontFamilyProperty, new FontFamily(family));
            FontFamilyBox.SelectedItem = null;
        }
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFormatBar || FontSizeBox.SelectedItem is not string sizeText)
            return;
        if (double.TryParse(sizeText, out var size) && size >= 4 && size <= 300)
            ApplySelectionProperty(TextElement.FontSizeProperty, size);
    }

    private void FontSizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || FontSizeBox.Text is not { Length: > 0 } sizeText)
            return;
        if (double.TryParse(sizeText, out var size) && size >= 4 && size <= 300)
        {
            ApplySelectionProperty(TextElement.FontSizeProperty, size);
            e.Handled = true;
        }
    }

    private void TextColorButton_Click(object sender, RoutedEventArgs e)
        => ShowColorPopup(brush => ApplySelectionProperty(TextElement.ForegroundProperty, brush));

    private void HighlightButton_Click(object sender, RoutedEventArgs e)
        => ShowColorPopup(brush => ApplySelectionProperty(TextElement.BackgroundProperty, brush));

    private void ShowColorPopup(Action<SolidColorBrush> apply)
    {
        var palette = new[] { "#000000", "#404040", "#808080", "#C0C0C0", "#FFFFFF",
                              "#C42B1C", "#E74C3C", "#F39C12", "#F1C40F", "#27AE60",
                              "#16A085", "#0067C0", "#2D5C9B", "#8E44AD", "#D6336C", "#8B4513" };

        var popup = new Window
        {
            Title = Get("SelectColorTitle"),
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Owner = this
        };
        var wrap = new WrapPanel { Margin = new Thickness(10), MaxWidth = 260 };
        foreach (var hex in palette)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hex));
            var swatch = new Button
            {
                Width = 24, Height = 24, Margin = new Thickness(2),
                Background = brush, BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            var captured = brush;
            swatch.Click += (_, _) => { apply(captured); popup.Close(); };
            wrap.Children.Add(swatch);
        }
        popup.Content = wrap;
        popup.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) popup.Close(); };

        if (sender is Button button)
        {
            var origin = button.PointToScreen(new Point(0, button.ActualHeight));
            popup.Left = origin.X - 40;
            popup.Top = origin.Y + 4;
        }
        popup.ShowDialog();
    }

    private void InsertImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null)
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Get("ImageFiles"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        InsertImageFromFile(dialog.FileName);
    }

    private void InsertVideoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null)
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Get("VideoFiles"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        InsertVideoFromFile(dialog.FileName);
    }

    private void InsertImageFromFile(string path)
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
    }

    private void InsertVideoFromFile(string path)
    {
        if (_active is null)
            return;
        try
        {
            var media = new System.Windows.Controls.MediaElement
            {
                Source = new Uri(path, UriKind.Absolute),
                Width = 480,
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                ScrubbingEnabled = true,
                Margin = new Thickness(2)
            };
            var playButton = new Button
            {
                Content = "\u25B6  " + System.IO.Path.GetFileName(path),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand
            };
            playButton.Click += (_, _) =>
            {
                if (media.CurrentState == MediaState.Play)
                    media.Pause();
                else
                    media.Play();
            };
            var panel = new StackPanel { Tag = path, Margin = new Thickness(2) };
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
            FocusActiveEditor();
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenError", path, ex.Message));
        }
    }

    private void Editor_PreviewDragOver(object sender, DragEventArgs e)
    {
        // media files become inline content; everything else opens as tabs
        e.Effects = IsMediaFileDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Editor_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!IsMediaFileDrop(e.Data))
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
            return;
        e.Handled = true;
        foreach (var file in files)
        {
            if (IsImageFile(file))
                InsertImageFromFile(file);
            else if (IsVideoFile(file))
                InsertVideoFromFile(file);
        }
    }

    private static bool IsMediaFileDrop(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop) &&
           data.GetData(DataFormats.FileDrop) is string[] files &&
           files.Length == 1 && (IsImageFile(files[0]) || IsVideoFile(files[0]));

    private static bool IsImageFile(string path)
        => Path.GetExtension(path) is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";

    private static bool IsVideoFile(string path)
        => Path.GetExtension(path) is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov";

    private void UpdateFormatToolbarState(DocumentTab tab)
    {
        if (!ReferenceEquals(_active, tab))
            return;
        _syncingFormatBar = true;
        try
        {
            var selection = tab.Editor.Selection;

            var weight = GetSelectionValue<FontWeight>(tab.Editor, TextElement.FontWeightProperty);
            BoldButton.Background = weight == FontWeights.Bold
                ? TryFindResource("C.Tab.Selected") as Brush ?? System.Windows.Media.Brushes.LightGray
                : System.Windows.Media.Brushes.Transparent;
            var style = GetSelectionValue<FontStyle>(tab.Editor, TextElement.FontStyleProperty);
            ItalicButton.Background = style == FontStyles.Italic
                ? TryFindResource("C.Tab.Selected") as Brush ?? System.Windows.Media.Brushes.LightGray
                : System.Windows.Media.Brushes.Transparent;

            var family = GetSelectionValue<FontFamily>(tab.Editor, TextElement.FontFamilyProperty);
            FontFamilyBox.SelectedItem = family is null ? null
                : Fonts.SystemFontFamilies.FirstOrDefault(f =>
                      string.Equals(f.Source, family.Source, StringComparison.OrdinalIgnoreCase));

            var size = GetSelectionValue<double>(tab.Editor, TextElement.FontSizeProperty);
            FontSizeBox.Text = size > 0 ? size.ToString("0.#") : string.Empty;
        }
        finally { _syncingFormatBar = false; }
    }

    // ==================== HTML render mode (WebView2) ====================

    private Microsoft.Web.WebView2.Wpf.WebView2? _webView;
    private int _renderGeneration;

    private void RenderHtmlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null)
            return;
        _active.RenderHtml = RenderHtmlMenuItem.IsChecked;
        UpdateRenderPane();
    }

    private void UpdateRenderPane()
    {
        var tab = _active;
        RenderHtmlMenuItem.IsChecked = tab?.RenderHtml == true;
        if (tab is null || !tab.RenderHtml)
        {
            RenderPane.Visibility = Visibility.Collapsed;
            return;
        }
        RenderPane.Visibility = Visibility.Visible;
        _ = ShowHtmlInWebViewAsync(tab);
    }

    private async System.Threading.Tasks.Task ShowHtmlInWebViewAsync(DocumentTab tab)
    {
        var generation = ++_renderGeneration;
        string html;
        var ext = tab.FilePath is null ? string.Empty : Path.GetExtension(tab.FilePath).ToLowerInvariant();
        if (ext is ".html" or ".htm")
            html = tab.Editor.GetText(); // the open document IS the HTML source
        else
            html = HtmlExporter.Export(tab.Editor.Document);

        try
        {
            if (_webView is null)
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ArkaSoft.Notepad", "webview");
                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                    .CreateAsync(null, userDataFolder);
                _webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                WebViewHost.Child = _webView;
                await _webView.EnsureCoreWebView2Async(environment);
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            }

            if (generation != _renderGeneration)
                return;
            RenderMessage.Visibility = Visibility.Collapsed;
            _webView.CoreWebView2.NavigateToString(html);
        }
        catch (Exception ex)
        {
            WebViewHost.Child = null;
            RenderMessage.Text = Get("RenderUnavailable", ex.Message);
            RenderMessage.Visibility = Visibility.Visible;
        }
    }

    private void OpenEmojiPanel()"""

s = s.replace(anchor, code)
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("handlers added")
