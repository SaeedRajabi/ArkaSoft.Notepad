using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ArkaSoft.Notepad.UI.Controls;
using ArkaSoft.Notepad.UI.Dialogs;
using ArkaSoft.Notepad.UI.Helpers;
using ArkaSoft.Notepad.UI.Models;
using ArkaSoft.Notepad.UI.Services;
using Microsoft.Win32;

namespace ArkaSoft.Notepad.UI;

public partial class MainWindow : Window
{
    private const double BaseEditorFont = 14.0;
    private const double NoWrapPageWidth = 100000.0;
    private const int WM_SETTINGCHANGE = 0x001C;

    private static readonly HashSet<string> RichEditingCommandNames =
        typeof(EditingCommands).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(RoutedUICommand))
            .Select(p => ((RoutedUICommand)p.GetValue(null)!).Name)
            .ToHashSet();

    private readonly AppSettings _settings;
    private readonly ObservableCollection<DocumentTab> _tabs = new();
    private DocumentTab? _active;
    private double _zoomPercent = 100;
    private bool _suppressTextEvents;
    private string _lastFindTerm = string.Empty;
    private bool _lastMatchCase;
    private DispatcherTimer? _highlightTimer;
    private bool _iconClickHandled;

    public MainWindow(AppSettings settings, IEnumerable<string>? initialFiles = null)
    {
        InitializeComponent();
        _settings = settings;

        DocTabs.ItemsSource = _tabs;

        WireFindPanel();
        ApplyStartupSettings(settings);

        if (initialFiles is not null)
        {
            foreach (var file in initialFiles)
                OpenFile(file);
        }
        if (_tabs.Count == 0)
            AddNewTab();

        Loaded += (_, _) =>
        {
            FocusActiveEditor();
            UpdateStatus();
        };
        StateChanged += (_, _) => OnStateChanged();
        ThemeService.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    // ==================== tab & editor management ====================

    private DocumentTab? AddNewTab(string? path = null)
    {
        var text = string.Empty;
        var encoding = FileService.Utf8NoBom;
        var encodingLabel = "UTF-8";
        if (path is not null)
        {
            try
            {
                var opened = FileService.Open(path);
                text = opened.Text;
                encoding = opened.Encoding;
                encodingLabel = opened.EncodingLabel;
            }
            catch (Exception ex)
            {
                MessageDialog.ShowInfo(this, "Notepad",
                    $"Could not open '{path}':{Environment.NewLine}{ex.Message}");
                return null;
            }
        }

        _suppressTextEvents = true;
        var editor = new RichTextBox { BorderThickness = new Thickness(0) };
        editor.Document = CreateDocument(text);

        var tab = new DocumentTab(editor, encoding, encodingLabel, path)
        {
            IsRightToLeft = LooksRightToLeft(text)
        };
        WireEditor(tab);
        ApplyReadingDirection(tab);

        _tabs.Add(tab);
        _suppressTextEvents = false;

        DocTabs.SelectedItem = tab;
        return tab;
    }

    private FlowDocument CreateDocument(string text)
    {
        var doc = new FlowDocument
        {
            FontFamily = (FontFamily)FindResource("F.Editor"),
            FontSize = BaseEditorFont * _zoomPercent / 100.0,
            PagePadding = new Thickness(10, 8, 10, 8),
            LineHeight = double.NaN
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "C.Text");
        ApplyWordWrapTo(doc, _settings.WordWrap);

        if (text.Length > 0)
            new TextRange(doc.ContentStart, doc.ContentEnd).Text = text;
        return doc;
    }

    private void WireEditor(DocumentTab tab)
    {
        var editor = tab.Editor;
        editor.TextChanged += (_, _) => OnTabTextChanged(tab);
        editor.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_active, tab))
                UpdateStatus();
        };
        editor.GotFocus += (_, _) => UpdateStatus();

        DataObject.AddPastingHandler(editor, PastePlainTextOnly);
        editor.AddHandler(CommandManager.PreviewCanExecuteEvent,
            new CanExecuteRoutedEventHandler(BlockRichEditingCommands), true);
        editor.ContextMenu = BuildEditorContextMenu(tab);
    }

    private void PastePlainTextOnly(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText, true))
            e.FormatToApply = DataFormats.UnicodeText;
        else if (e.DataObject.GetDataPresent(DataFormats.Text, true))
            e.FormatToApply = DataFormats.Text;
        else
            e.CancelCommand();
    }

    private void BlockRichEditingCommands(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command is RoutedUICommand command && RichEditingCommandNames.Contains(command.Name))
        {
            e.CanExecute = false;
            e.Handled = true;
        }
    }

    private ContextMenu BuildEditorContextMenu(DocumentTab tab)
    {
        var menu = new ContextMenu();

        MenuItem Mk(string header, ICommand? command = null, RoutedEventHandler? click = null,
            bool checkable = false)
        {
            var item = new MenuItem { Header = header };
            if (command is not null)
                item.Command = command;
            if (click is not null)
                item.Click += click;
            if (checkable)
                item.IsCheckable = true;
            menu.Items.Add(item);
            return item;
        }

        Mk("Undo", ApplicationCommands.Undo);
        Mk("Cut", ApplicationCommands.Cut);
        Mk("Copy", ApplicationCommands.Copy);
        Mk("Paste", ApplicationCommands.Paste);
        Mk("Delete", ApplicationCommands.Delete);
        menu.Items.Add(new Separator());
        Mk("Select all", ApplicationCommands.SelectAll);
        var rtl = Mk("Right-to-left reading direction", click: (_, _) => ToggleReadingDirection(), checkable: true);
        menu.Items.Add(new Separator());
        Mk("Emoji", click: (_, _) => OpenEmojiPanel());

        menu.Opened += (_, _) =>
        {
            rtl.IsChecked = tab.IsRightToLeft;
        };
        return menu;
    }

    private void OnTabTextChanged(DocumentTab tab)
    {
        if (_suppressTextEvents)
            return;
        tab.IsDirty = true;
        if (ReferenceEquals(_active, tab))
            UpdateWindowTitle();
        UpdateStatusFor(tab);

        if (FindPanel.Visibility == Visibility.Visible)
        {
            _highlightTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _highlightTimer.Tick -= HighlightTimer_Tick;
            _highlightTimer.Tick += HighlightTimer_Tick;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }
    }

    private void HighlightTimer_Tick(object? sender, EventArgs e)
    {
        _highlightTimer?.Stop();
        RefreshFindHighlights();
    }

    private void DocTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Equals(DocTabs.SelectedItem, _active))
            return;

        if (_active is not null)
            SaveScroll(_active);

        _active = DocTabs.SelectedItem as DocumentTab;
        if (_active is null)
            return;

        _suppressTextEvents = true;
        EditorHost.Content = _active.Editor;
        _suppressTextEvents = false;

        Dispatcher.BeginInvoke(() =>
        {
            RestoreScroll(_active);
            FocusActiveEditor();
            UpdateStatus();
        }, DispatcherPriority.Loaded);

        UpdateWindowTitle();
        UpdateStatus();
        UpdateMenuChecks();
    }

    private void DocTabs_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || e.ButtonState != MouseButtonState.Pressed)
            return;
        if (e.OriginalSource is DependencyObject source)
        {
            var tabItem = FindAncestor<TabItem>(source);
            if (tabItem?.DataContext is DocumentTab tab)
            {
                CloseTab(tab);
                e.Handled = true;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T matched)
                return matched;
            source = VisualTreeHelper.GetParent(source) is { } visual
                ? visual
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private static ScrollViewer? GetViewer(RichTextBox editor)
        => editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;

    private void SaveScroll(DocumentTab tab)
    {
        if (GetViewer(tab.Editor) is { } viewer)
        {
            tab.ScrollVertical = viewer.VerticalOffset;
            tab.ScrollHorizontal = viewer.HorizontalOffset;
        }
    }

    private void RestoreScroll(DocumentTab tab)
    {
        if (GetViewer(tab.Editor) is { } viewer)
        {
            viewer.ScrollToVerticalOffset(tab.ScrollVertical);
            viewer.ScrollToHorizontalOffset(tab.ScrollHorizontal);
        }
    }

    private void FocusActiveEditor() => _active?.Editor.Focus();

    // ==================== file operations ====================

    private bool OpenFile(string path)
    {
        try
        {
            path = System.IO.Path.GetFullPath(path);
        }
        catch
        {
            // keep as-is
        }

        var existing = _tabs.FirstOrDefault(t =>
            t.FilePath is not null &&
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            DocTabs.SelectedItem = existing;
            return true;
        }
        return AddNewTab(path) is not null;
    }

    private void OpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text documents (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            foreach (var name in dialog.FileNames)
                OpenFile(name);
        }
    }

    private bool SaveTab(DocumentTab tab)
    {
        if (tab.FilePath is null)
            return SaveTabAs(tab);

        try
        {
            FileService.Save(tab.FilePath, tab.Editor.GetText(), tab.Encoding);
            tab.IsDirty = false;
            UpdateWindowTitle();
            UpdateStatusFor(tab);
            return true;
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, "Notepad",
                $"Could not save '{tab.FilePath}':{Environment.NewLine}{ex.Message}");
            return false;
        }
    }

    private bool SaveTabAs(DocumentTab tab)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text documents (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = tab.FilePath is not null
                ? System.IO.Path.GetFileName(tab.FilePath)
                : "Untitled.txt",
            InitialDirectory = tab.FilePath is not null
                ? System.IO.Path.GetDirectoryName(tab.FilePath)
                : null
        };
        if (dialog.ShowDialog(this) != true)
            return false;

        var label = EncodingDialog.Show(this, tab.EncodingLabel);
        if (label is null)
            return false;

        var option = FileService.FindOption(label) ?? FileService.EncodingChoices[0];
        try
        {
            FileService.Save(dialog.FileName, tab.Editor.GetText(), option.Create());
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, "Notepad",
                $"Could not save '{dialog.FileName}':{Environment.NewLine}{ex.Message}");
            return false;
        }

        tab.FilePath = System.IO.Path.GetFullPath(dialog.FileName);
        tab.Encoding = option.Create();
        tab.EncodingLabel = label;
        tab.IsDirty = false;
        UpdateWindowTitle();
        UpdateMenuChecks();
        UpdateStatusFor(tab);
        return true;
    }

    private void CloseTab(DocumentTab tab)
    {
        if (tab.IsDirty)
        {
            DocTabs.SelectedItem = tab;
            var result = MessageDialog.Show(this, "Notepad",
                $"Do you want to save changes to:{Environment.NewLine}{tab.FileName}",
                MessageDialogButtonSet.SaveDiscardCancel);
            if (result == MessageDialogResult.Cancel)
                return;
            if (result == MessageDialogResult.Primary && !SaveTab(tab))
                return;
        }

        _tabs.Remove(tab);
        if (_tabs.Count == 0)
        {
            Close();
            return;
        }
        if (ReferenceEquals(DocTabs.SelectedItem, null) || _active == tab)
        {
            DocTabs.SelectedItem = _tabs[Math.Max(0, _tabs.Count - 1)];
        }
        UpdateWindowTitle();
    }

    private void RenameTab(DocumentTab tab)
    {
        if (tab.FilePath is null)
        {
            MessageDialog.ShowInfo(this, "Notepad",
                "Save the file before renaming it.");
            return;
        }

        var invalid = new string(System.IO.Path.GetInvalidFileNameChars());
        var newName = InputDialog.Show(this, "Rename", "File name:", tab.FileName, value =>
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Enter a file name.";
            if (value.IndexOfAny(invalid.ToCharArray()) >= 0)
                return $"A file name cannot contain any of these characters: {invalid}";
            if (!value.Contains('.'))
                return null;
            return null;
        });
        if (newName is null)
            return;

        var directory = System.IO.Path.GetDirectoryName(tab.FilePath);
        var newPath = System.IO.Path.Combine(directory ?? Environment.CurrentDirectory, newName);
        try
        {
            FileService.Rename(tab.FilePath, newPath);
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, "Notepad",
                $"Could not rename the file:{Environment.NewLine}{ex.Message}");
            return;
        }
        tab.FilePath = newPath;
        UpdateWindowTitle();
        UpdateMenuChecks();
    }

    // ==================== find / replace ====================

    private void WireFindPanel()
    {
        FindPanel.FindNext = () => PerformFind(backward: false);
        FindPanel.FindPrevious = () => PerformFind(backward: true);
        FindPanel.ReplaceOne = PerformReplaceOne;
        FindPanel.ReplaceAll = PerformReplaceAll;
        FindPanel.CloseRequested = CloseFindPanel;
        FindPanel.OptionsChanged = () =>
        {
            _lastFindTerm = FindPanel.SearchText;
            _lastMatchCase = FindPanel.MatchCase;
            RefreshFindHighlights();
        };
    }

    private void CloseFindPanel()
    {
        FindPanel.Close();
        if (_active is not null)
            ClearTabHighlights(_active);
        FocusActiveEditor();
    }

    private (string term, bool matchCase) GetFindOptions()
    {
        if (FindPanel.Visibility == Visibility.Visible)
            return (FindPanel.SearchText, FindPanel.MatchCase);
        return (_lastFindTerm, _lastMatchCase);
    }

    private void PerformFind(bool backward)
    {
        var tab = _active;
        if (tab is null)
            return;
        var (term, matchCase) = GetFindOptions();
        if (string.IsNullOrEmpty(term))
        {
            FindPanel.Open(false, string.Empty);
            return;
        }
        _lastFindTerm = term;
        _lastMatchCase = matchCase;

        var text = tab.Editor.GetText();
        var indexes = RichTextHelper.FindAll(text, term, matchCase);
        HighlightAll(tab, indexes, term.Length);

        if (indexes.Length == 0)
        {
            FindPanel.UpdateCount(0, 0);
            return;
        }

        var doc = tab.Editor.Document;
        int from = backward
            ? RichTextHelper.IndexOf(doc, tab.Editor.Selection.Start)
            : RichTextHelper.IndexOf(doc, tab.Editor.Selection.End);

        int targetIndex;
        if (backward)
        {
            targetIndex = indexes.LastOrDefault(i => i + term.Length <= from);
            if (targetIndex < 0)
                targetIndex = indexes[^1]; // wrap around
        }
        else
        {
            targetIndex = indexes.FirstOrDefault(i => i >= from);
            if (targetIndex < 0)
                targetIndex = indexes[0]; // wrap around
        }

        var start = RichTextHelper.PointerAtIndex(doc, targetIndex);
        var end = RichTextHelper.PointerAtIndex(doc, targetIndex + term.Length);
        tab.Editor.Selection.Select(start, end);
        RichTextHelper.ScrollCaretIntoView(tab.Editor);

        FindPanel.UpdateCount(Array.IndexOf(indexes, targetIndex) + 1, indexes.Length);
    }

    private void RefreshFindHighlights()
    {
        var tab = _active;
        if (tab is null || FindPanel.Visibility != Visibility.Visible)
            return;
        var (term, matchCase) = GetFindOptions();
        if (string.IsNullOrEmpty(term))
        {
            ClearTabHighlights(tab);
            FindPanel.UpdateCount(0, 0);
            return;
        }
        var indexes = RichTextHelper.FindAll(tab.Editor.GetText(), term, matchCase);
        HighlightAll(tab, indexes, term.Length);

        int current = 0;
        var selected = new TextRange(tab.Editor.Selection.Start, tab.Editor.Selection.End).Text;
        if (string.Equals(selected, term, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            var doc = tab.Editor.Document;
            var selStart = RichTextHelper.IndexOf(doc, tab.Editor.Selection.Start);
            current = Array.FindIndex(indexes, i => i == selStart) + 1;
        }
        FindPanel.UpdateCount(current, indexes.Length);
    }

    private void HighlightAll(DocumentTab tab, int[] indexes, int length)
    {
        var doc = tab.Editor.Document;
        var whole = new TextRange(doc.ContentStart, doc.ContentEnd);
        whole.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        whole.ApplyPropertyValue(TextElement.ForegroundProperty, null);
        tab.ClearHighlights();

        if (indexes.Length == 0)
            return;

        var background = FindResource("C.Highlight.Bg");
        var foreground = FindResource("C.Highlight.Fg");
        foreach (var index in indexes)
        {
            var start = RichTextHelper.PointerAtIndex(doc, index);
            var end = RichTextHelper.PointerAtIndex(doc, index + length);
            if (end.CompareTo(start) <= 0)
                continue;
            var range = new TextRange(start, end);
            range.ApplyPropertyValue(TextElement.BackgroundProperty, background);
            range.ApplyPropertyValue(TextElement.ForegroundProperty, foreground);
        }
        tab.SetHighlights(indexes.Select(i => (i, length)));
    }

    private void ClearTabHighlights(DocumentTab tab)
    {
        var doc = tab.Editor.Document;
        var whole = new TextRange(doc.ContentStart, doc.ContentEnd);
        whole.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        whole.ApplyPropertyValue(TextElement.ForegroundProperty, null);
        tab.ClearHighlights();
    }

    private void PerformReplaceOne()
    {
        var tab = _active;
        if (tab is null)
            return;
        var (term, matchCase) = GetFindOptions();
        if (string.IsNullOrEmpty(term))
            return;

        var selection = new TextRange(tab.Editor.Selection.Start, tab.Editor.Selection.End);
        if (string.Equals(selection.Text, term, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            selection.Text = FindPanel.ReplaceText;

        PerformFind(backward: false);
    }

    private void PerformReplaceAll()
    {
        var tab = _active;
        if (tab is null)
            return;
        var (term, matchCase) = GetFindOptions();
        if (string.IsNullOrEmpty(term))
            return;

        var text = tab.Editor.GetText();
        var count = RichTextHelper.FindAll(text, term, matchCase).Length;
        if (count == 0)
        {
            FindPanel.UpdateCount(0, 0);
            return;
        }

        string replaced = matchCase
            ? text.Replace(term, FindPanel.ReplaceText)
            : Regex.Replace(text, Regex.Escape(term), _ => FindPanel.ReplaceText, RegexOptions.IgnoreCase);

        _suppressTextEvents = true;
        tab.Editor.SetText(replaced);
        _suppressTextEvents = false;
        tab.IsDirty = true;
        UpdateWindowTitle();
        UpdateStatusFor(tab);
        FindPanel.UpdateCount(0, 0);
        FindPanel.ShowNote($"{count} replaced");
    }

    // ==================== zoom / wrap / reading direction ====================

    private void ApplyZoom(double percent)
    {
        _zoomPercent = Math.Clamp(percent, 10, 500);
        var size = BaseEditorFont * _zoomPercent / 100.0;
        foreach (var tab in _tabs)
        {
            tab.Editor.FontSize = size;
            tab.Editor.Document.FontSize = size;
        }
        _settings.ZoomPercent = _zoomPercent;
        UpdateZoomLabels();
    }

    private void UpdateZoomLabels()
        => ZoomStatus.Content = $"{(int)Math.Round(_zoomPercent)}%";

    private void ApplyWordWrapTo(FlowDocument doc, bool wrap)
        => doc.PageWidth = wrap ? double.NaN : NoWrapPageWidth;

    private void SetWordWrap(bool wrap)
    {
        _settings.WordWrap = wrap;
        WordWrapMenuItem.IsChecked = wrap;
        foreach (var tab in _tabs)
            ApplyWordWrapTo(tab.Editor.Document, wrap);
        UpdateStatus();
    }

    private void ApplyReadingDirection(DocumentTab tab)
    {
        var direction = tab.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        tab.Editor.FlowDirection = direction;
        tab.Editor.Document.FlowDirection = direction;
    }

    private void ToggleReadingDirection()
    {
        if (_active is null)
            return;
        _active.IsRightToLeft = !_active.IsRightToLeft;
        ApplyReadingDirection(_active);
        FocusActiveEditor();
    }

    private static bool LooksRightToLeft(string text)
    {
        var limit = Math.Min(text.Length, 500);
        for (int i = 0; i < limit; i++)
        {
            var c = text[i];
            if (c is >= '\u0590' and <= '\u05FF' or >= '\u0600' and <= '\u06FF' or >= '\u0700' and <= '\u074F')
                return true;
            if (char.IsLetter(c))
                return false;
        }
        return false;
    }

    private void OpenEmojiPanel()
    {
        try
        {
            const byte vkLwin = 0x5B;
            const byte vkPeriod = 0xBE;
            const uint keyUp = 0x0002;
            keybd_event(vkLwin, 0, 0, UIntPtr.Zero);
            keybd_event(vkPeriod, 0, 0, UIntPtr.Zero);
            keybd_event(vkPeriod, 0, keyUp, UIntPtr.Zero);
            keybd_event(vkLwin, 0, keyUp, UIntPtr.Zero);
        }
        catch
        {
            // emoji hotkey is best effort
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // ==================== status / title / menu state ====================

    private void UpdateWindowTitle()
        => Title = _active is null
            ? "Notepad"
            : $"{(_active.IsDirty ? "*" : string.Empty)}{_active.FileName} - Notepad";

    private void UpdateStatus() => UpdateStatusFor(_active);

    private void UpdateStatusFor(DocumentTab? tab)
    {
        if (tab is null)
        {
            LnText.Text = "Ln 1";
            ColText.Text = ", Col 1";
            LinesText.Text = "1 line";
            CharsText.Text = "0 characters";
            EncodingText.Text = "UTF-8";
            return;
        }

        var editor = tab.Editor;
        var text = editor.GetText();
        var chars = text.Replace("\r\n", "\n").Length;
        var lines = RichTextHelper.CountLines(editor);

        LinesText.Text = lines == 1 ? "1 line" : $"{lines} lines";
        CharsText.Text = chars == 1 ? "1 character" : $"{chars} characters";
        EncodingText.Text = tab.EncodingLabel;

        if (_settings.WordWrap)
        {
            LnColPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            LnColPanel.Visibility = Visibility.Visible;
            var (line, column) = RichTextHelper.GetLineColumn(editor);
            LnText.Text = $"Ln {line}";
            ColText.Text = $", Col {column}";
        }
        UpdateZoomLabels();
    }

    private void UpdateMenuChecks()
    {
        var theme = (AppTheme)_settings.Theme;
        ThemeSystemMenuItem.IsChecked = theme == AppTheme.System;
        ThemeLightMenuItem.IsChecked = theme == AppTheme.Light;
        ThemeDarkMenuItem.IsChecked = theme == AppTheme.Dark;
        RenameMenuItem.IsEnabled = _active?.FilePath is not null;
    }

    // ==================== startup / persistence ====================

    private void ApplyStartupSettings(AppSettings settings)
    {
        _zoomPercent = Math.Clamp(settings.ZoomPercent, 10, 500);
        UpdateZoomLabels();
        WordWrapMenuItem.IsChecked = settings.WordWrap;
        StatusBarMenuItem.IsChecked = settings.ShowStatusBar;
        StatusBar.Visibility = settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        UpdateMenuChecks();

        var bounds = settings.Window;
        if (bounds is not null && bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
            if (bounds.Left > virtualLeft - bounds.Width &&
                bounds.Left < virtualRight &&
                bounds.Top > virtualTop - bounds.Height &&
                bounds.Top < virtualBottom)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = bounds.Left;
                Top = bounds.Top;
                Width = bounds.Width;
                Height = bounds.Height;
            }
            if (bounds.Maximized)
                WindowState = WindowState.Maximized;
        }
        OnStateChanged();
    }

    private void PersistSettings()
    {
        _settings.WordWrap = WordWrapMenuItem.IsChecked;
        _settings.ShowStatusBar = StatusBarMenuItem.Visibility == Visibility.Visible;
        _settings.ZoomPercent = _zoomPercent;
        _settings.SessionFiles = _tabs
            .Where(t => t.FilePath is not null)
            .Select(t => t.FilePath!)
            .ToList();

        if (WindowState == WindowState.Normal)
        {
            _settings.Window = new WindowBounds
            {
                Left = Left, Top = Top, Width = Width, Height = Height,
                Maximized = false
            };
        }
        else
        {
            var restore = RestoreBounds;
            _settings.Window = new WindowBounds
            {
                Left = restore.Left, Top = restore.Top, Width = restore.Width, Height = restore.Height,
                Maximized = true
            };
        }

        SettingsService.Save(_settings);
    }

    // ==================== window chrome & messages ====================

    private void OnStateChanged()
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(7, 7, 7, 0)
            : new Thickness(0);
    }

    private void OnEffectiveThemeChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_active is not null && FindPanel.Visibility == Visibility.Visible && _lastFindTerm.Length > 0)
                RefreshFindHighlights();
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE)
            ThemeService.OnSystemSettingChanged();
        return IntPtr.Zero;
    }

    // ==================== command handlers ====================

    private void NewTab_Executed(object sender, ExecutedRoutedEventArgs e) => AddNewTab();

    private void NewWindow_Executed(object sender, ExecutedRoutedEventArgs e)
        => new MainWindow(_settings).Show();

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => OpenDialog();

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is not null)
            SaveTab(_active);
    }

    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is not null)
            SaveTabAs(_active);
    }

    private void Rename_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is not null)
            RenameTab(_active);
    }

    private void PageSetup_Executed(object sender, ExecutedRoutedEventArgs e)
        => PageSetupDialog.Show(this, _settings.Print);

    private void Print_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is not null)
            PrintService.Print(this, _active.Editor, _settings.Print);
    }

    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) => Close();

    private void CloseTab_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is DocumentTab tab)
            CloseTab(tab);
        else if (_active is not null)
            CloseTab(_active);
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var initial = string.Empty;
        if (_active is not null)
        {
            var selection = new TextRange(_active.Editor.Selection.Start, _active.Editor.Selection.End).Text;
            if (selection.Length is > 0 and < 100 && !selection.Contains('\n'))
                initial = selection;
        }
        FindPanel.Open(false, initial);
        if (!string.IsNullOrEmpty(FindPanel.SearchText))
        {
            _lastFindTerm = FindPanel.SearchText;
            _lastMatchCase = FindPanel.MatchCase;
            RefreshFindHighlights();
        }
    }

    private void Replace_Executed(object sender, ExecutedRoutedEventArgs e)
        => FindPanel.Open(true, _lastFindTerm);

    private void FindNext_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (FindPanel.Visibility != Visibility.Visible && _lastFindTerm.Length == 0)
        {
            Find_Executed(sender, e);
            return;
        }
        PerformFind(backward: false);
    }

    private void FindPrevious_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (FindPanel.Visibility != Visibility.Visible && _lastFindTerm.Length == 0)
        {
            Find_Executed(sender, e);
            return;
        }
        PerformFind(backward: true);
    }

    private void GoTo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var tab = _active;
        if (tab is null)
            return;
        if (_settings.WordWrap)
        {
            MessageDialog.ShowInfo(this, "Notepad",
                "Go To is unavailable when word wrap is enabled.");
            return;
        }

        var totalLines = RichTextHelper.CountLines(tab.Editor);
        var input = InputDialog.Show(this, "Go to", $"Line number (1 - {totalLines}):", "1", value =>
        {
            if (!int.TryParse(value, out var line) || line < 1 || line > totalLines)
                return $"Enter a line number between 1 and {totalLines}.";
            return null;
        });
        if (input is null || !int.TryParse(input, out var target) || target < 1)
            return;

        var text = tab.Editor.GetText();
        int index = 0;
        for (int line = 1; line < target && index >= 0; line++)
            index = text.IndexOf('\n', index) + 1;
        if (index < 0)
            return;

        var caret = RichTextHelper.PointerAtIndex(tab.Editor.Document, index);
        tab.Editor.Selection.Select(caret, caret);
        tab.Editor.CaretPosition = caret;
        RichTextHelper.ScrollCaretIntoView(tab.Editor);
        FocusActiveEditor();
    }

    private void InsertTimeDate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var tab = _active;
        if (tab is null)
            return;
        var stamp = DateTime.Now.ToString("HH:mm dd/MM/yyyy");
        tab.Editor.BeginChange();
        var caret = tab.Editor.CaretPosition;
        if (!tab.Editor.Selection.IsEmpty)
            new TextRange(tab.Editor.Selection.Start, tab.Editor.Selection.End).Text = string.Empty;
        caret.InsertTextInRun(stamp);
        tab.Editor.EndChange();
        tab.Editor.CaretPosition = caret.GetPositionAtOffset(stamp.Length) ?? tab.Editor.Document.ContentEnd;
        RichTextHelper.ScrollCaretIntoView(tab.Editor);
    }

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => ApplyZoom(_zoomPercent + 10);

    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => ApplyZoom(_zoomPercent - 10);

    private void ZoomReset_Executed(object sender, ExecutedRoutedEventArgs e) => ApplyZoom(100);

    private void WordWrap_Executed(object sender, ExecutedRoutedEventArgs e)
        => SetWordWrap(WordWrapMenuItem.IsChecked);

    private void StatusBar_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var visible = StatusBarMenuItem.IsChecked;
        StatusBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _settings.ShowStatusBar = visible;
    }

    private void ThemeSystem_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _settings.Theme = (int)AppTheme.System;
        ThemeService.Apply(AppTheme.System);
        UpdateMenuChecks();
    }

    private void ThemeLight_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _settings.Theme = (int)AppTheme.Light;
        ThemeService.Apply(AppTheme.Light);
        UpdateMenuChecks();
    }

    private void ThemeDark_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _settings.Theme = (int)AppTheme.Dark;
        ThemeService.Apply(AppTheme.Dark);
        UpdateMenuChecks();
    }

    private void NextTab_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_tabs.Count < 2)
            return;
        var index = _tabs.IndexOf(_active!);
        DocTabs.SelectedItem = _tabs[(index + 1) % _tabs.Count];
    }

    private void PreviousTab_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_tabs.Count < 2)
            return;
        var index = _tabs.IndexOf(_active!);
        DocTabs.SelectedItem = _tabs[(index - 1 + _tabs.Count) % _tabs.Count];
    }

    // ==================== window events ====================

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AppIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _iconClickHandled = false;
        if (e.ClickCount == 2)
        {
            _iconClickHandled = true;
            Close();
        }
    }

    private void AppIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_iconClickHandled)
            return;
        if (sender is FrameworkElement element)
            SystemCommands.ShowSystemMenu(this, element.PointToScreen(new Point(0, 34)));
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ApplyZoom(_zoomPercent + Math.Sign(e.Delta) * 10);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
                OpenFile(file);
            e.Handled = true;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        foreach (var tab in _tabs.Where(t => t.IsDirty).ToList())
        {
            DocTabs.SelectedItem = tab;
            var result = MessageDialog.Show(this, "Notepad",
                $"Do you want to save changes to:{Environment.NewLine}{tab.FileName}",
                MessageDialogButtonSet.SaveDiscardCancel);
            if (result == MessageDialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == MessageDialogResult.Primary && !SaveTab(tab))
            {
                e.Cancel = true;
                return;
            }
            tab.IsDirty = false;
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        ThemeService.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        PersistSettings();
    }
}
