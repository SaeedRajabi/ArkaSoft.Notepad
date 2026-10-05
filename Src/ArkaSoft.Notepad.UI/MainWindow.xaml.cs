using ArkaSoft.Notepad.UI.Controls;
using ArkaSoft.Notepad.UI.Dialogs;
using ArkaSoft.Notepad.UI.Helpers;
using ArkaSoft.Notepad.UI.Models;
using ArkaSoft.Notepad.UI.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using static ArkaSoft.Notepad.UI.Services.LocalizationService;

namespace ArkaSoft.Notepad.UI;

public partial class MainWindow : Window
{
    private const double BaseEditorFont = 14.0;
    private const int WM_SETTINGCHANGE = 0x001C;
    private const int WM_INPUTLANGCHANGE = 0x0051;

    private readonly AppSettings _settings;
    private readonly ObservableCollection<DocumentTab> _tabs = new();
    private DocumentTab? _active;
    private double _zoomPercent = 100;
    private bool _suppressTextEvents;
    private string _lastFindTerm = string.Empty;
    private bool _lastMatchCase;
    private DispatcherTimer? _highlightTimer;
    private bool _iconClickHandled;
    private readonly DispatcherTimer _maintenanceTimer;
    private Point? _tabDragStart;
    private DocumentTab? _tabBeingDragged;
    private bool _languageShortcutPressed;
    private SidebarMode _sidebarMode = SidebarMode.Pinned;
    private bool _sidebarOverlayOpen;
    private DispatcherTimer? _sidebarCloseTimer;
    private DispatcherTimer? _outlineTimer;

    public MainWindow(AppSettings settings, IEnumerable<string>? initialFiles = null,
        SessionState? session = null)
    {
        InitializeComponent();
        _settings = settings;

        DocTabs.ItemsSource = _tabs;

        FontFamilyBox.ItemsSource = Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontSizeBox.ItemsSource = new[]
            { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };
        _syncingFormatBar = true;
        var defaultFamily = Fonts.SystemFontFamilies.FirstOrDefault(f =>
            string.Equals(f.Source, _settings.EnglishEditorFont, StringComparison.OrdinalIgnoreCase));
        FontFamilyBox.SelectedItem = defaultFamily
            ?? Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Segoe UI");
        FontSizeBox.SelectedItem = "14";
        _syncingFormatBar = false;
        DocTabs.ItemContainerGenerator.StatusChanged += (_, _) => MarkTabHeadersInteractive();

        WireFindPanel();
        WireSidebar();
        ApplyStartupSettings(settings);

        if (initialFiles is not null)
        {
            foreach (var file in initialFiles)
                OpenFile(file);
        }
        if (session is not null)
            RestoreSessionTabs(session);
        if (_tabs.Count == 0)
            AddNewTab();
        RestoreSessionMenuItem.IsChecked = settings.RestoreSession;
        LineNumbersMenuItem.IsChecked = settings.ShowLineNumbers;

        Loaded += (_, _) =>
        {
            MarkTabHeadersInteractive();
            FocusActiveEditor();
            UpdateStatus();
            RefreshOutline();
        };
        StateChanged += (_, _) => OnStateChanged();
        ThemeService.EffectiveThemeChanged += OnEffectiveThemeChanged;
        InputLanguageManager.Current.InputLanguageChanged += InputLanguage_Changed;
        LocalizationService.Changed += OnLanguageChanged;

        // periodic housekeeping: session autosave + disk change detection
        _maintenanceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _maintenanceTimer.Tick += (_, _) =>
        {
            if (_settings.RestoreSession)
                SaveSessionSnapshot();
            if (_active is not null && ReferenceEquals(EditorHost.Content, _active.Host))
                CheckDiskStamp(_active, announce: true);
        };
        _maintenanceTimer.Start();
    }

    // ==================== tab & editor management ====================

    private void RestoreSessionTabs(SessionState session)
    {
        foreach (var tabState in session.Tabs)
        {
            DocumentTab? tab;
            var draftText = SessionService.ReadDraft(tabState.DraftFile);
            if (tabState.FilePath is not null && File.Exists(tabState.FilePath))
            {
                tab = AddNewTab(tabState.FilePath);
                if (tab is not null && draftText is not null)
                {
                    // the draft holds newer unsaved edits than the file on disk
                    _suppressTextEvents = true;
                    tab.Editor.SetText(draftText);
                    tab.LastText = draftText;
                    _suppressTextEvents = false;
                    tab.IsDirty = true;
                }
            }
            else
            {
                tab = AddNewTab(null, tabState.DraftFile);
            }

            if (tab is null)
                continue;

            var option = FileService.FindOption(tabState.EncodingLabel);
            if (option is not null)
            {
                tab.Encoding = option.Create();
                tab.EncodingLabel = tabState.EncodingLabel;
            }
            tab.IsRightToLeft = tabState.IsRightToLeft;
            // restore an EXPLICITLY chosen mode only (1=RTL, 2=LTR). The legacy
            // caret-based IsRightToLeft bool must never force the whole document.
            tab.TextLayout.Mode = tabState.DirectionMode switch
            {
                1 => TextDirectionMode.Rtl,
                2 => TextDirectionMode.Ltr,
                _ => TextDirectionMode.Normal
            };
            if (tab.TextLayout.Mode != TextDirectionMode.Normal)
                tab.TextLayout.InputDirection = tab.TextLayout.Mode == TextDirectionMode.Rtl
                    ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            tab.ScrollVertical = tabState.ScrollVertical;
            tab.ScrollHorizontal = tabState.ScrollHorizontal;

            if (tabState.FilePath is not null)
                AddRecentFile(tabState.FilePath);
        }

        if (session.ActiveIndex >= 0 && session.ActiveIndex < _tabs.Count)
            DocTabs.SelectedItem = _tabs[session.ActiveIndex];
    }

    private DocumentTab? AddNewTab(string? path = null, string? draftOverride = null)
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
                MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenError", path, ex.Message));
                return null;
            }
        }
        else if (draftOverride is not null)
        {
            text = SessionService.ReadDraft(draftOverride) ?? string.Empty;
        }

        _suppressTextEvents = true;
        var editor = new RichTextBox { BorderThickness = new Thickness(0), FlowDirection = FlowDirection.LeftToRight };
        editor.SetResourceReference(Control.BackgroundProperty, "C.Surface");
        editor.SetResourceReference(Control.ForegroundProperty, "C.Text");
        editor.SetResourceReference(RichTextBox.CaretBrushProperty, "C.Text");
        editor.SetResourceReference(RichTextBox.SelectionBrushProperty, "C.Selection");
        editor.Document = CreateDocument(text);

        var tab = new DocumentTab(editor, encoding, encodingLabel, path)
        {
            LastText = text
        };
        tab.Host = BuildTabHost(tab);
        tab.TextLayout.InputDirection = BilingualText.DirectionFor(InputLanguageManager.Current.CurrentInputLanguage);
        RefreshEditorPresentation(tab);
        WireEditor(tab);
        ApplyEditorFont(tab);
        if (path is not null)
            StampLastWrite(tab);

        _tabs.Add(tab);
        _suppressTextEvents = false;

        DocTabs.SelectedItem = tab;
        return tab;
    }

    /// <summary>Builds the Grid that shows the editor beside its line-number gutter.</summary>
    private Grid BuildTabHost(DocumentTab tab)
    {
        var host = new Grid { FlowDirection = FlowDirection.LeftToRight };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Canvas CreateGutter()
        {
            var gutter = new Canvas
            {
                Background = Brushes.Transparent,
                ClipToBounds = true,
                Width = 42,
                IsHitTestVisible = false,
                Visibility = _settings.ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed
            };
            gutter.SetResourceReference(Canvas.BackgroundProperty, "C.Window");
            gutter.SetResourceReference(TextBlock.FontFamilyProperty, "F.Ui");
            return gutter;
        }

        tab.Gutter = CreateGutter();
        tab.RightGutter = CreateGutter();
        Grid.SetColumn(tab.Gutter, 0);
        Grid.SetColumn(tab.Editor, 1);
        Grid.SetColumn(tab.RightGutter, 2);
        host.Children.Add(tab.Gutter);
        host.Children.Add(tab.Editor);
        host.Children.Add(tab.RightGutter);

        // Do not render on LayoutUpdated: it fires repeatedly while typing and
        // used to recreate one visual for every visible line on every frame.
        tab.Editor.Loaded += (_, _) => ScheduleGutterUpdate(tab);
        tab.Editor.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => ScheduleGutterUpdate(tab)));
        return host;
    }

    private void ScheduleGutterUpdate(DocumentTab tab)
    {
        if (!_settings.ShowLineNumbers || tab.GutterUpdateQueued)
            return;

        tab.GutterUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            tab.GutterUpdateQueued = false;
            UpdateGutter(tab);
        }, DispatcherPriority.Render);
    }

    private void UpdateGutter(DocumentTab tab)
    {
        if (!_settings.ShowLineNumbers || !tab.Editor.IsLoaded)
            return;

        var viewer = GetViewer(tab.Editor);
        if (viewer is null)
            return;

        var totalLines = RichTextHelper.CountLines(tab.Editor);
        var foreground = TryFindResource("C.TextDisabled") as Brush ?? Brushes.Gray;
        var widest = totalLines.ToString().Length;
        var unitWidth = Math.Max(9, tab.Editor.FontSize) * 0.66;
        var gutterWidth = Math.Max(34, widest * unitWidth + 18);
        tab.Gutter.Width = gutterWidth;
        tab.RightGutter.Width = gutterWidth;

        // TextPointer coordinates are the actual visual line positions after
        // WPF has applied the selected font, zoom, DPI and line spacing. Using
        // those coordinates avoids the cumulative drift caused by an estimated
        // line height.
        tab.Gutter.Children.Clear();
        tab.RightGutter.Children.Clear();
        var line = 0;
        foreach (var paragraph in tab.Editor.Document.Blocks.OfType<Paragraph>())
        {
            line++;
            var rect = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
                continue;
            if (rect.Bottom < 0)
                continue;
            if (rect.Top > tab.Editor.ActualHeight)
                break;

            var isRightToLeft = paragraph.FlowDirection == FlowDirection.RightToLeft;
            var label = new TextBlock
            {
                Text = line.ToString(CultureInfo.InvariantCulture),
                Width = gutterWidth - 8,
                TextAlignment = isRightToLeft ? TextAlignment.Left : TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                FontSize = Math.Max(9, tab.Editor.FontSize),
                Foreground = foreground
            };
            label.Measure(new Size(label.Width, double.PositiveInfinity));
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, rect.Top + Math.Max(0, (rect.Height - label.DesiredSize.Height) / 2));
            (isRightToLeft ? tab.RightGutter : tab.Gutter).Children.Add(label);
        }
    }

    private FlowDocument CreateDocument(string text)
    {
        var doc = new FlowDocument
        {
            FlowDirection = FlowDirection.LeftToRight,
            FontSize = BaseEditorFont * _zoomPercent / 100.0,
            PagePadding = new Thickness(10, 8, 10, 8),
            LineHeight = double.NaN
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "C.Text");
        doc.SetResourceReference(TextElement.FontFamilyProperty, "F.Editor");
        NumberSubstitution.SetSubstitution(doc, NumberSubstitutionMethod.European);

        if (text.Length > 0)
            new TextRange(doc.ContentStart, doc.ContentEnd).Text = text;
        return doc;
    }

    private void WireEditor(DocumentTab tab)
    {
        var editor = tab.Editor;
        editor.PreviewTextInput += (_, e) => ApplyTypedTextDirection(tab, e.Text);
        editor.PreviewKeyDown += (_, e) => TrackLanguageShortcut(e);
        editor.PreviewKeyUp += (_, e) => ApplyLanguageShortcutIfCompleted(e);
        editor.TextChanged += (_, _) => OnTabTextChanged(tab);
        editor.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_active, tab))
            {
                tab.IsRightToLeft = editor.CaretPosition.Paragraph?.FlowDirection == FlowDirection.RightToLeft;
                UpdateStatus();
                UpdateOutlineActiveLine();
                UpdateFormatToolbarState(tab);
            }
        };
        editor.GotFocus += (_, _) => UpdateStatus();
        editor.GotFocus += (_, _) => ApplyInputLanguageDirection();
        editor.SizeChanged += (_, _) => RefreshEditorPresentation(tab);

        // rich text: accept images/videos dropped onto the editor
        editor.AllowDrop = true;
        editor.PreviewDragOver += Editor_PreviewDragOver;
        editor.PreviewDrop += Editor_PreviewDrop;

        editor.ContextMenu = BuildEditorContextMenu(tab);
    }

    private void TrackLanguageShortcut(KeyEventArgs e)
    {
        if ((e.Key is Key.LeftAlt or Key.RightAlt) &&
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ||
            (e.Key is Key.LeftShift or Key.RightShift) &&
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            _languageShortcutPressed = true;
        }
    }

    private void ApplyLanguageShortcutIfCompleted(KeyEventArgs e)
    {
        if (!_languageShortcutPressed || e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift))
            return;

        _languageShortcutPressed = false;
        // Windows switches the keyboard layout after the shortcut key is
        // released, so defer until the input queue is idle before reading it.
        Dispatcher.BeginInvoke(() => ApplyInputLanguageDirection(), DispatcherPriority.ContextIdle);
    }

    private void ApplyTypedTextDirection(DocumentTab tab, string text)
    {
        if (_suppressTextEvents)
            return;
        // Forced RTL/LTR modes pin every paragraph on Refresh; nothing per-keystroke.
        if (tab.TextLayout.Mode != TextDirectionMode.Normal)
            return;

        var direction = BilingualText.DetectDirection(text);
        if (direction is null)
            return;

        // Only the START of a paragraph follows the typing language. Mid-sentence
        // language switches keep the sentence's base direction — no left/right
        // flip and no font jump. TextChanged -> Refresh applies the direction
        // again after the first Run exists.
        tab.TextLayout.InputDirection = direction.Value;
        tab.TextLayout.ApplyInputDirectionToEmptyCaretParagraph(direction.Value);
    }

    private ContextMenu BuildEditorContextMenu(DocumentTab tab)
    {
        var menu = new ContextMenu();

        MenuItem Mk(string header, ICommand? command = null, RoutedEventHandler? click = null,
            bool checkable = false)
        {
            var item = new MenuItem();
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "T." + header);
            item.SetResourceReference(FlowDirectionProperty, "Ui.Direction");
            if (command is not null)
            {
                item.Command = command;
                item.CommandTarget = tab.Editor;
            }
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
        Mk("SelectAll", ApplicationCommands.SelectAll);
        var normalItem = Mk("DirectionNormal", click: (_, _) => SetDirectionMode(TextDirectionMode.Normal), checkable: true);
        var rtlItem = Mk("RTL", click: (_, _) => SetDirectionMode(TextDirectionMode.Rtl), checkable: true);
        var ltrItem = Mk("LTR", click: (_, _) => SetDirectionMode(TextDirectionMode.Ltr), checkable: true);
        menu.Items.Add(new Separator());
        Mk("Emoji", click: (_, _) => OpenEmojiPanel());

        menu.Opened += (_, _) =>
        {
            var mode = tab.TextLayout.Mode;
            normalItem.IsChecked = mode == TextDirectionMode.Normal;
            rtlItem.IsChecked = mode == TextDirectionMode.Rtl;
            ltrItem.IsChecked = mode == TextDirectionMode.Ltr;
        };
        return menu;
    }

    private void OnTabTextChanged(DocumentTab tab)
    {
        if (_suppressTextEvents)
            return;
        var text = tab.Editor.GetText();
        if (text == tab.LastText)
            return; // Presentation changes must not mark a text file as modified.
        tab.LastText = text;
        RefreshEditorPresentation(tab);
        tab.IsDirty = true;
        if (ReferenceEquals(_active, tab))
            UpdateWindowTitle();
        UpdateStatusFor(tab);
        ScheduleOutlineRefresh();

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
        EditorHost.Content = _active.Host;
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
        RefreshOutline();
        UpdateOutlineActiveLine();
        UpdateRenderPane();
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

    private void DocTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindAncestor<Button>(source) is not null)
            return;

        var tabItem = FindAncestor<TabItem>(source);
        if (tabItem?.DataContext is not DocumentTab tab)
            return;

        // The title bar uses WindowChrome, so select explicitly rather than
        // depending on TabItem's normal mouse handling being reached.
        DocTabs.SelectedItem = tab;
        _tabBeingDragged = tab;
        _tabDragStart = e.GetPosition(DocTabs);
    }

    private void DocTabs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabBeingDragged is null || _tabDragStart is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(DocTabs);
        var distance = point - _tabDragStart.Value;
        if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var tab = _tabBeingDragged;
        _tabBeingDragged = null;
        _tabDragStart = null;
        DragDrop.DoDragDrop(DocTabs, new DataObject(typeof(DocumentTab), tab), DragDropEffects.Move);
    }

    private void DocTabs_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DocumentTab))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DocTabs_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DocumentTab)) is not DocumentTab dragged ||
            e.OriginalSource is not DependencyObject source)
            return;

        var targetItem = FindAncestor<TabItem>(source);
        var target = targetItem?.DataContext as DocumentTab;
        if (target is null || targetItem is null || ReferenceEquals(target, dragged))
            return;

        var oldIndex = _tabs.IndexOf(dragged);
        var targetIndex = _tabs.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0)
            return;

        // Dropping on the right half puts the tab after its target; dropping on
        // the left half puts it before, making both reorder directions explicit.
        if (e.GetPosition(targetItem).X > targetItem.ActualWidth / 2)
            targetIndex++;
        if (oldIndex < targetIndex)
            targetIndex--;
        if (targetIndex != oldIndex)
            _tabs.Move(oldIndex, targetIndex);
        DocTabs.SelectedItem = dragged;
        e.Handled = true;
    }

    private void MarkTabHeadersInteractive()
    {
        foreach (var tab in _tabs)
        {
            if (DocTabs.ItemContainerGenerator.ContainerFromItem(tab) is TabItem item)
                WindowChrome.SetIsHitTestVisibleInChrome(item, true);
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
        => editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer
           ?? FindVisualDescendant<ScrollViewer>(editor);

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;
            if (FindVisualDescendant<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

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

    // ==================== explorer sidebar ====================

    private const double SidebarDefaultWidth = 280;
    private const double SidebarStripWidth = 8;
    private double _sidebarWidth = SidebarDefaultWidth;

    private void WireSidebar()
    {
        Sidebar.PinClick += () => SetSidebarMode(SidebarMode.Pinned);
        Sidebar.AutoClick += () => SetSidebarMode(SidebarMode.AutoHide);
        Sidebar.CloseClick += () => SetSidebarMode(SidebarMode.Closed);
        Sidebar.StripMouseEnter += () =>
        {
            if (_sidebarMode == SidebarMode.AutoHide)
                OpenSidebarOverlay();
        };
        Sidebar.PanelMouseEnter += () =>
        {
            _sidebarCloseTimer?.Stop();
        };
        Sidebar.PanelMouseLeave += () =>
        {
            if (_sidebarMode == SidebarMode.AutoHide)
                ScheduleSidebarClose();
        };
        Sidebar.LineClicked += NavigateToOutlineLine;
        Sidebar.ToolbarOpenFolder += OpenWorkspaceFolderDialog;
        Sidebar.ToolbarNewFile += () => CreateWorkspaceItem(isFolder: false, parentOverride: null);
        Sidebar.ToolbarNewFolder += () => CreateWorkspaceItem(isFolder: true, parentOverride: null);
        Sidebar.ToolbarRefresh += ReloadWorkspace;
        Sidebar.NodeAction += SidebarNodeAction_Executed;
        Sidebar.ItemCreateRequested += CreateWorkspaceItemOnDisk;
        Sidebar.ResizePreview += Sidebar_ResizePreview;
        Sidebar.HoldChanged += Sidebar_HoldChanged;
    }

    private void Sidebar_ResizePreview(double pointerXInWindow)
    {
        // the panel sits on the visual right in Persian, so width mirrors the pointer
        var isPersian = LocalizationService.Language == "fa";
        var target = isPersian ? ActualWidth - pointerXInWindow : pointerXInWindow;
        _sidebarWidth = Math.Clamp(target, 180, Math.Max(240, ActualWidth - 180));
        ApplySidebarLayout();
    }

    private void Sidebar_HoldChanged()
    {
        // a menu or editor just ended: close again if the mouse already left,
        // unless a modal dialog keeps the user busy
        if (_sidebarMode == SidebarMode.AutoHide && !Sidebar.HoldOpen &&
            !Sidebar.IsMouseOver && !HasOpenOwnedWindow())
        {
            ScheduleSidebarClose();
        }
    }

    // ==================== workspace (file tree) operations ====================

    private string? _workspacePath;

    private void OpenWorkspaceFolderDialog()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Get("OpenFolder"),
            InitialDirectory = _workspacePath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == true)
            OpenWorkspaceFolder(dialog.FolderName);
    }

    private void OpenWorkspaceFolder(string path)
    {
        if (!Directory.Exists(path))
            return;
        _workspacePath = path;
        Sidebar.ChildrenProvider = LoadChildrenFor;
        Sidebar.ShowWorkspace(BuildWorkspaceRoot(path));
        _settings.OpenedFolder = path;
    }

    private void CloseWorkspace()
    {
        _workspacePath = null;
        Sidebar.CloseWorkspace();
        _settings.OpenedFolder = null;
    }

    private FileSystemNode BuildWorkspaceRoot(string folderPath)
    {
        var node = FileSystemNode.CreateFolder(
            Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            folderPath);
        MarkChildrenPresence(node);
        return node;
    }

    private void MarkChildrenPresence(FileSystemNode folder)
    {
        folder.Children.Clear();
        folder.ChildrenLoaded = false;
        try
        {
            bool any = Directory.EnumerateDirectories(folder.FullPath).Any() ||
                       Directory.EnumerateFiles(folder.FullPath).Any();
            if (any)
                folder.Children.Add(FileSystemNode.CreatePlaceholder());
        }
        catch
        {
            // unreadable folder shows as empty
        }
    }

    private IEnumerable<FileSystemNode> LoadChildrenFor(FileSystemNode folder)
    {
        var list = new List<FileSystemNode>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder.FullPath))
            {
                var child = FileSystemNode.CreateFolder(Path.GetFileName(dir), dir);
                MarkChildrenPresence(child);
                list.Add(child);
            }
            foreach (var file in Directory.EnumerateFiles(folder.FullPath))
                list.Add(CreateFileNode(file));
        }
        catch
        {
            // access denied etc.: show what we have
        }
        list.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        folder.ChildrenLoaded = true;
        return list;
    }

    private static FileSystemNode CreateFileNode(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var node = FileSystemNode.CreateFile(name, filePath);
        var (label, background, foreground) = FileIconHelper.For(name);
        if (label.Length > 0)
        {
            node.IconLabel = label;
            node.IconBrush = FileIconHelper.Brush(background);
            node.IconForeground = FileIconHelper.Brush(foreground);
        }
        return node;
    }

    private void ReloadWorkspace()
    {
        if (_workspacePath is null)
            return;
        var expanded = Sidebar.CollectExpandedPaths();
        Sidebar.ShowWorkspace(BuildWorkspaceRoot(_workspacePath));
        Sidebar.RestoreExpanded(expanded);
    }

    private void SidebarNodeAction_Executed(FileSystemNode node, SidebarNodeAction action)
    {
        switch (action)
        {
            case SidebarNodeAction.Open:
                if (!node.IsFolder && File.Exists(node.FullPath))
                    OpenFile(node.FullPath);
                else if (node.IsFolder)
                    node.IsExpanded = !node.IsExpanded;
                break;
            case SidebarNodeAction.NewFile:
                if (_workspacePath is null)
                {
                    MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenFolderFirst"));
                    break;
                }
                Sidebar.BeginCreateItem(Sidebar.ResolveParentFor(node) ?? Sidebar.RootNode!, isFolder: false);
                break;
            case SidebarNodeAction.NewFolder:
                if (_workspacePath is null)
                {
                    MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenFolderFirst"));
                    break;
                }
                Sidebar.BeginCreateItem(Sidebar.ResolveParentFor(node) ?? Sidebar.RootNode!, isFolder: true);
                break;
            case SidebarNodeAction.Rename:
                RenameWorkspaceItem(node);
                break;
            case SidebarNodeAction.Delete:
                DeleteWorkspaceItem(node);
                break;
            case SidebarNodeAction.Reveal:
                RevealInFileExplorer(node.FullPath);
                break;
        }
    }

    private void CreateWorkspaceItem(bool isFolder, string? parentOverride)
    {
        if (_workspacePath is null)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("OpenFolderFirst"));
            return;
        }

        // resolve the target folder NODE so the inline editor lands inside the tree
        var parentNode = parentOverride is not null
            ? FindNodeByPath(Sidebar.RootNode, parentOverride) ?? Sidebar.RootNode
            : Sidebar.SelectedFsNode is { } selected
                ? Sidebar.ResolveParentFor(selected) ?? Sidebar.RootNode
                : Sidebar.RootNode;
        if (parentNode is null)
            return;
        Sidebar.BeginCreateItem(parentNode, isFolder);
    }

    private static FileSystemNode? FindNodeByPath(FileSystemNode? node, string path)
    {
        if (node is null)
            return null;
        if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
            return node;
        foreach (var child in node.Children)
        {
            var found = FindNodeByPath(child, path);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void CreateWorkspaceItemOnDisk(string parentPath, string name, bool isFolder)
    {
        var fullPath = Path.Combine(parentPath, name);
        try
        {
            if (isFolder)
                Directory.CreateDirectory(fullPath);
            else
                File.WriteAllText(fullPath, string.Empty);
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("CantCreate", name, ex.Message));
        }
        ReloadWorkspace();
    }

    private void RenameWorkspaceItem(FileSystemNode node)
    {
        var oldPath = node.FullPath;
        var parent = Path.GetDirectoryName(oldPath);
        if (parent is null)
            return;

        while (true)
        {
            var result = RenameDialog.Show(this, Get("Rename"), Get("RenameHelp"), node.Name, node.IsFolder);
            if (result is null)
                return;
            var (name, ext) = result.Value;
            var newName = name + (ext.Length > 0 ? "." + ext : string.Empty);
            if (string.Equals(newName, node.Name, StringComparison.Ordinal))
                return;

            if (File.Exists(Path.Combine(parent, newName)) || Directory.Exists(Path.Combine(parent, newName)))
            {
                MessageDialog.ShowInfo(this, Get("AppName"), Get("NameTaken"));
                continue;
            }

            var newPath = Path.Combine(parent, newName);
            try
            {
                if (node.IsFolder)
                    Directory.Move(oldPath, newPath);
                else
                    File.Move(oldPath, newPath, overwrite: false);
            }
            catch (Exception ex)
            {
                MessageDialog.ShowInfo(this, Get("AppName"), Get("RenameError", ex.Message));
                return;
            }

            // remap open tabs under the renamed path
            foreach (var tab in _tabs)
            {
                if (tab.FilePath is not null &&
                    tab.FilePath.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    tab.FilePath = newPath + tab.FilePath[oldPath.Length..];
                }
            }
            UpdateWindowTitle();
            ReloadWorkspace();
            return;
        }
    }

    private void DeleteWorkspaceItem(FileSystemNode node)
    {
        var result = MessageDialog.Show(this, Get("AppName"),
            Get("DeleteConfirm", node.Name), MessageDialogButtonSet.YesNo);
        if (result != MessageDialogResult.Primary)
            return;

        try
        {
            if (node.IsFolder)
                Directory.Delete(node.FullPath, recursive: true);
            else
                File.Delete(node.FullPath);
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("CantDelete", node.Name, ex.Message));
            return;
        }

        if (string.Equals(node.FullPath, _workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            CloseWorkspace();
            return;
        }

        // close tabs that pointed at deleted content
        foreach (var tab in _tabs.Where(t =>
                t.FilePath is not null &&
                (string.Equals(t.FilePath, node.FullPath, StringComparison.OrdinalIgnoreCase) ||
                 t.FilePath.StartsWith(node.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .ToList())
        {
            _tabs.Remove(tab);
        }
        if (_tabs.Count == 0)
            AddNewTab();
        if (_active is null || !_tabs.Contains(_active))
            DocTabs.SelectedItem = _tabs[^1];
        UpdateWindowTitle();
        ReloadWorkspace();
    }

    private void RevealInFileExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // explorer launch is best effort
        }
    }


    private void SetSidebarMode(SidebarMode mode)
    {
        _sidebarMode = mode;
        _sidebarOverlayOpen = false;
        _sidebarCloseTimer?.Stop();
        ApplySidebarLayout();
        UpdateMenuChecks();
        if (mode != SidebarMode.Closed)
            RefreshOutline();
    }

    private void OpenSidebarOverlay()
    {
        if (_sidebarMode != SidebarMode.AutoHide)
            return;
        _sidebarOverlayOpen = true;
        _sidebarCloseTimer?.Stop();
        ApplySidebarLayout();
        RefreshOutline();
    }

    private void ScheduleSidebarClose()
    {
        // never start the close countdown while a menu, the inline editor,
        // or a dialog keeps the sidebar logically in use
        if (Sidebar.HoldOpen)
            return;
        _sidebarCloseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _sidebarCloseTimer.Tick += CloseTimer_Tick;
        _sidebarCloseTimer.Tick -= CloseTimer_Tick;
        _sidebarCloseTimer.Tick += CloseTimer_Tick;
        _sidebarCloseTimer.Stop();
        _sidebarCloseTimer.Start();
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        _sidebarCloseTimer?.Stop();
        if (_sidebarMode != SidebarMode.AutoHide)
            return;
        // keep the panel while the mouse is over it, a menu/editor is active,
        // or a modal dialog owned by this window is on screen
        if (Sidebar.IsMouseOver || Sidebar.HoldOpen || HasOpenOwnedWindow())
            return;
        _sidebarOverlayOpen = false;
        ApplySidebarLayout();
    }

    private bool HasOpenOwnedWindow()
        => OwnedWindows.OfType<Window>().Any(w => w.IsVisible);

    /// <summary>Lays the sidebar out: physical side follows the UI language
    /// (left in English, right in Persian), pinned docks it, auto-hide shows
    /// only a hover strip that opens as an overlay.</summary>
    private void ApplySidebarLayout()
    {
        // the editor-area container is forced LTR, so alignments and margins
        // are physical here: Right really is the right edge in Persian too
        var isPersian = LocalizationService.Language == "fa";
        Sidebar.HorizontalAlignment = isPersian
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        switch (_sidebarMode)
        {
            case SidebarMode.Closed:
                Sidebar.Visibility = Visibility.Collapsed;
                EditorArea.Margin = new Thickness(0);
                break;

            case SidebarMode.AutoHide:
                Sidebar.Visibility = Visibility.Visible;
                Sidebar.ShowStrip = !_sidebarOverlayOpen;
                Sidebar.Width = _sidebarOverlayOpen ? _sidebarWidth : SidebarStripWidth;
                Sidebar.Effect = _sidebarOverlayOpen
                    ? (System.Windows.Media.Effects.Effect)FindResource("Fx.Popup")
                    : null;
                // overlay: the editor keeps its full width
                EditorArea.Margin = new Thickness(0);
                break;

            case SidebarMode.Pinned:
                Sidebar.Visibility = Visibility.Visible;
                Sidebar.ShowStrip = false;
                Sidebar.Width = _sidebarWidth;
                Sidebar.Effect = null;
                EditorArea.Margin = isPersian
                    ? new Thickness(0, 0, _sidebarWidth, 0)
                    : new Thickness(_sidebarWidth, 0, 0, 0);
                break;
        }
    }

    private void ToggleSidebar_Executed(object sender, ExecutedRoutedEventArgs e)
        => SetSidebarMode(_sidebarMode == SidebarMode.Pinned
            ? SidebarMode.Closed
            : SidebarMode.Pinned);

    /// <summary>Highlights the outline entry at the caret (status bar Ln/Col reuse).</summary>
    private void UpdateOutlineActiveLine()
    {
        if (_sidebarMode == SidebarMode.Closed || _active is null || _workspacePath is not null)
            return;
        var (line, _) = RichTextHelper.GetLineColumn(_active.Editor);
        Sidebar.SetActiveLine(Math.Max(0, line - 1));
    }

    private void NavigateToOutlineLine(int line)
    {
        var tab = _active;
        if (tab is null)
            return;
        var text = tab.Editor.GetText();
        int index = 0;
        for (int current = 0; current < line && index >= 0; current++)
            index = text.IndexOf('\n', index) + 1;
        if (index < 0 || index > text.Length)
            index = text.Length;

        var caret = RichTextHelper.PointerAtIndex(tab.Editor.Document, index);
        tab.Editor.Selection.Select(caret, caret);
        tab.Editor.CaretPosition = caret;
        RichTextHelper.ScrollCaretIntoView(tab.Editor);
        FocusActiveEditor();
        Sidebar.SetActiveLine(line);
    }

    /// <summary>Rebuilds the outline for the active tab (debounced while typing).</summary>
    private void RefreshOutline()
    {
        if (_workspacePath is not null)
            return; // file tree is showing; the outline is not active
        var tab = _active;
        if (tab is null)
        {
            Sidebar.LoadOutline(string.Empty, new List<OutlineNode>());
            return;
        }
        List<OutlineNode> nodes;
        try
        {
            var text = tab.Editor.GetText();
            if (text.Length > 200_000)
                text = text[..200_000];
            nodes = DocumentOutline.Build(text);
        }
        catch
        {
            nodes = new List<OutlineNode>();
        }
        Sidebar.LoadOutline(tab.FileName, nodes);
    }

    private void ScheduleOutlineRefresh()
    {
        if (_sidebarMode == SidebarMode.Closed)
            return;
        _outlineTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _outlineTimer.Tick -= OutlineTimer_Tick;
        _outlineTimer.Tick += OutlineTimer_Tick;
        _outlineTimer.Stop();
        _outlineTimer.Start();
    }

    private void OutlineTimer_Tick(object? sender, EventArgs e)
    {
        _outlineTimer?.Stop();
        if (_sidebarMode != SidebarMode.Closed && Sidebar.Visibility == Visibility.Visible)
            RefreshOutline();
    }

    // ==================== second-instance & activation ====================

    /// <summary>Opens files forwarded by a second launch of the app.</summary>
    public void OpenFilesFromSecondInstance(string[] files)
    {
        WindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        Activate();
        foreach (var file in files)
            OpenFile(file);
    }

    /// <summary>Activates the window when a second launch happens without files.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        FocusActiveEditor();
    }

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
            CheckDiskStamp(existing, announce: false);
            return true;
        }
        var added = AddNewTab(path);
        if (added is not null)
            AddRecentFile(path);
        return added is not null;
    }

    private void StampLastWrite(DocumentTab tab)
    {
        try
        {
            if (tab.FilePath is not null && File.Exists(tab.FilePath))
                tab.LastWriteStamp = File.GetLastWriteTime(tab.FilePath);
            tab.PendingDiskChange = false;
        }
        catch
        {
            // unreadable stamp just disables change detection for this tab
        }
    }

    /// <summary>Detects out-of-band file modifications. With announce=true (focus, activate,
    /// timer) the user is asked; with announce=false the tab silently marks pending.</summary>
    private void CheckDiskStamp(DocumentTab tab, bool announce)
    {
        if (tab.FilePath is null)
            return;
        try
        {
            if (!File.Exists(tab.FilePath))
            {
                if (!tab.PendingDiskChange && announce)
                {
                    tab.PendingDiskChange = true;
                    MessageDialog.ShowInfo(this, Get("AppName"),
                        Get("FileDeletedKeep", System.IO.Path.GetFileName(tab.FilePath)));
                }
                else
                {
                    tab.PendingDiskChange = true;
                }
                return;
            }

            var stamp = File.GetLastWriteTime(tab.FilePath);
            if (stamp != tab.LastWriteStamp && tab.LastWriteStamp != default)
            {
                tab.LastWriteStamp = stamp;
                if (tab.IsDirty)
                {
                    tab.PendingDiskChange = true;
                    if (announce)
                        MessageDialog.ShowInfo(this, Get("AppName"),
                            Get("FileChanged", System.IO.Path.GetFileName(tab.FilePath)));
                }
                else if (announce || FindPanel.Visibility != Visibility.Visible)
                {
                    ReloadTab(tab);
                }
                else
                {
                    tab.PendingDiskChange = true;
                }
            }
        }
        catch
        {
            // ignore stamp read failures
        }
    }

    private void ReloadTab(DocumentTab tab)
    {
        if (tab.FilePath is null)
            return;
        try
        {
            var opened = FileService.Open(tab.FilePath);
            var caretIndex = RichTextHelper.IndexOf(tab.Editor.Document, tab.Editor.CaretPosition);
            _suppressTextEvents = true;
            tab.Editor.SetText(opened.Text);
            tab.LastText = tab.Editor.GetText();
            tab.Encoding = opened.Encoding;
            tab.EncodingLabel = opened.EncodingLabel;
            var caret = RichTextHelper.PointerAtIndex(tab.Editor.Document,
                Math.Min(caretIndex, tab.Editor.GetText().Length));
            tab.Editor.CaretPosition = caret;
            RefreshEditorPresentation(tab);
            _suppressTextEvents = false;
            tab.IsDirty = false;
            StampLastWrite(tab);
            UpdateWindowTitle();
            UpdateStatusFor(tab);
        }
        catch
        {
            // if the reload fails keep the in-memory content
        }
    }

    private void OpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = Get("FileFilter"),
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
            StampLastWrite(tab);
            UpdateWindowTitle();
            UpdateStatusFor(tab);
            return true;
        }
        catch (Exception ex)
        {
            MessageDialog.ShowInfo(this, Get("AppName"), Get("SaveError", tab.FilePath!, ex.Message));
            return false;
        }
    }

    private bool SaveTabAs(DocumentTab tab)
    {
        var dialog = new SaveFileDialog
        {
            Filter = Get("FileFilter"),
            FileName = tab.FilePath is not null
                ? System.IO.Path.GetFileName(tab.FilePath)
                : Get("Untitled") + ".txt",
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
            MessageDialog.ShowInfo(this, Get("AppName"), Get("SaveError", dialog.FileName, ex.Message));
            return false;
        }

        tab.FilePath = System.IO.Path.GetFullPath(dialog.FileName);
        tab.Encoding = option.Create();
        tab.EncodingLabel = label;
        tab.IsDirty = false;
        StampLastWrite(tab);
        AddRecentFile(tab.FilePath);
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
            var result = MessageDialog.Show(this, Get("AppName"), Get("SaveChanges", tab.FileName),
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
            MessageDialog.ShowInfo(this, Get("AppName"), Get("SaveBeforeRename"));
            return;
        }

        var invalid = new string(System.IO.Path.GetInvalidFileNameChars());
        var newName = InputDialog.Show(this, Get("Rename"), Get("FileName"), tab.FileName, value =>
        {
            if (string.IsNullOrWhiteSpace(value))
                return Get("EnterFileName");
            if (value.IndexOfAny(invalid.ToCharArray()) >= 0)
                return Get("InvalidFileName", invalid);
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
            MessageDialog.ShowInfo(this, Get("AppName"), Get("RenameError", ex.Message));
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
        whole.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("C.Text"));
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
        whole.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("C.Text"));
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
        tab.LastText = tab.Editor.GetText();
        RefreshEditorPresentation(tab);
        _suppressTextEvents = false;
        tab.IsDirty = true;
        UpdateWindowTitle();
        UpdateStatusFor(tab);
        FindPanel.UpdateCount(0, 0);
        FindPanel.ShowNote(Get("Replaced", count));
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

            // headings were sized relative to the base font at click time —
            // rescale them with the zoom so the whole document grows/shrinks
            foreach (var paragraph in tab.Editor.Document.Blocks.OfType<Paragraph>())
            {
                if (paragraph.Tag is not string level)
                    continue;
                var factor = level switch
                {
                    "H1" => 1.7,
                    "H2" => 1.4,
                    "H3" => 1.15,
                    _ => 1.0
                };
                if (factor != 1.0)
                {
                    paragraph.FontSize = size * factor;
                    paragraph.LineHeight = paragraph.FontSize * 1.45;
                }
            }

            RefreshEditorPresentation(tab);
        }
        _settings.ZoomPercent = _zoomPercent;
        UpdateZoomLabels();
    }

    private void UpdateZoomLabels()
        => ZoomStatus.Content = $"{(int)Math.Round(_zoomPercent)}%";

    private void SetWordWrap(bool wrap)
    {
        _settings.WordWrap = wrap;
        WordWrapMenuItem.IsChecked = wrap;
        foreach (var tab in _tabs)
            RefreshEditorPresentation(tab);
        UpdateStatus();
    }

    private void InputLanguage_Changed(object sender, InputLanguageEventArgs e)
        => Dispatcher.BeginInvoke(() => ApplyInputLanguageDirection(), DispatcherPriority.Input);

    private void ApplyInputLanguageDirection(CultureInfo? culture = null)
    {
        if (_active is null || !_active.Editor.IsKeyboardFocusWithin)
            return;

        // In RTL/LTR modes the direction is pinned; keyboard layout switches
        // must not flip the document. In Normal mode only EMPTY paragraphs
        // follow the keyboard — mid-sentence switches change nothing.
        if (_active.TextLayout.Mode != TextDirectionMode.Normal)
            return;

        var direction = BilingualText.DirectionFor(culture ?? GetCurrentKeyboardCulture());
        _active.TextLayout.InputDirection = direction;
        // A new/empty paragraph has no strong character from which WPF can infer
        // direction. Give it the active keyboard direction so its caret starts at
        // the right in Persian and the left in English.
        _active.TextLayout.ApplyInputDirectionToEmptyCaretParagraph(direction);
        RefreshEditorPresentation(_active);
    }

    private static CultureInfo GetCurrentKeyboardCulture()
    {
        var languageId = (int)(GetKeyboardLayout(0).ToInt64() & 0xFFFF);
        try { return CultureInfo.GetCultureInfo(languageId); }
        catch (CultureNotFoundException) { return InputLanguageManager.Current.CurrentInputLanguage; }
    }

    private void RefreshEditorPresentation(DocumentTab tab)
    {
        var suppressed = _suppressTextEvents;
        _suppressTextEvents = true;
        try
        {
            // Uniform, deterministic line height: Vazir and Cascadia report
            // different natural metrics, so the automatic height made the
            // spacing vary between lines. BlockLineHeight pins every line.
            var doc = tab.Editor.Document;
            doc.LineHeight = doc.FontSize * 1.6;
            doc.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;

            tab.TextLayout.Refresh();
            tab.TextLayout.UpdatePageWidth(_settings.WordWrap);
            ScheduleGutterUpdate(tab);
        }
        finally { _suppressTextEvents = suppressed; }
    }

    private void ApplyEditorFont(DocumentTab tab)
    {
        tab.Editor.SetResourceReference(Control.FontFamilyProperty, "F.Editor");
        tab.Editor.Document.SetResourceReference(TextElement.FontFamilyProperty, "F.Editor");
        tab.TextLayout.PersianFont = TypographyService.PersianEditorFontFamily;
        tab.TextLayout.EnglishFont = TypographyService.EnglishEditorFontFamily;
    }

    private void ApplyTypographySettings()
    {
        TypographyService.Apply(_settings);
        foreach (var tab in _tabs)
        {
            ApplyEditorFont(tab);
            RefreshEditorPresentation(tab);
            ApplyParagraphFonts(tab);
        }
    }

    /// <summary>Re-syncs each paragraph's font to its current direction — needed
    /// after the user changes the Persian/English font settings.</summary>
    private static void ApplyParagraphFonts(DocumentTab tab)
    {
        foreach (var paragraph in tab.Editor.Document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.FlowDirection == FlowDirection.RightToLeft)
                paragraph.FontFamily = tab.TextLayout.PersianFont ?? paragraph.FontFamily;
            else
                paragraph.FontFamily = tab.TextLayout.EnglishFont ?? paragraph.FontFamily;
        }
    }

    /// <summary>Applies one of the three reading-direction modes. With a text
    /// selection only the selected paragraphs change; without one the whole
    /// document changes. Long documents are applied in dispatcher chunks so
    /// the UI thread never freezes.</summary>
    private void SetDirectionMode(TextDirectionMode mode)
    {
        if (_active is null)
            return;
        var tab = _active;
        tab.TextLayout.Mode = mode;
        if (mode == TextDirectionMode.Rtl)
            tab.TextLayout.InputDirection = FlowDirection.RightToLeft;
        else if (mode == TextDirectionMode.Ltr)
            tab.TextLayout.InputDirection = FlowDirection.LeftToRight;

        var selection = tab.Editor.Selection;
        if (!selection.IsEmpty)
        {
            ApplyDirectionToRange(tab, selection.Start, selection.End, mode);
            RefreshEditorPresentation(tab);
            MarkActiveDirty();
            FocusActiveEditor();
            return;
        }

        MarkActiveDirty();
        FocusActiveEditor();
        _ = ApplyDirectionToDocumentChunkedAsync(tab, mode);
    }

    /// <summary>Applies the mode to the paragraphs intersecting the selection.</summary>
    private void ApplyDirectionToRange(DocumentTab tab, TextPointer start, TextPointer end,
        TextDirectionMode mode)
    {
        foreach (var paragraph in tab.Editor.Document.Blocks.OfType<Paragraph>())
        {
            if (paragraph.ContentEnd.CompareTo(start) < 0 ||
                paragraph.ContentStart.CompareTo(end) > 0)
                continue;
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
            var direction = ResolveParagraphDirection(mode, text,
                string.IsNullOrWhiteSpace(text), tab.TextLayout.InputDirection);
            BilingualText.ApplyDirection(paragraph, direction,
                tab.TextLayout.PersianFont, tab.TextLayout.EnglishFont);
        }
    }

    private static FlowDirection ResolveParagraphDirection(TextDirectionMode mode, string text,
        bool empty, FlowDirection inputDirection)
        => mode switch
        {
            TextDirectionMode.Rtl => FlowDirection.RightToLeft,
            TextDirectionMode.Ltr => FlowDirection.LeftToRight,
            _ => empty ? inputDirection : BilingualText.DetectDirection(text) ?? inputDirection
        };

    /// <summary>Whole-document direction application in dispatcher chunks: the
    /// expensive TextRange reads are spread over background-priority frames so
    /// the window stays responsive on very long documents.</summary>
    private async System.Threading.Tasks.Task ApplyDirectionToDocumentChunkedAsync(
        DocumentTab tab, TextDirectionMode mode)
    {
        var generation = ++tab.DirectionApplyGeneration;
        var paragraphs = tab.Editor.Document.Blocks.OfType<Paragraph>().ToList();
        const int chunkSize = 150;
        int index = 0;

        while (index < paragraphs.Count)
        {
            if (generation != tab.DirectionApplyGeneration || !_tabs.Contains(tab))
                return;

            int end = Math.Min(index + chunkSize, paragraphs.Count);
            for (; index < end; index++)
            {
                var paragraph = paragraphs[index];
                var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                var direction = ResolveParagraphDirection(mode, text,
                    string.IsNullOrWhiteSpace(text), tab.TextLayout.InputDirection);
                BilingualText.ApplyDirection(paragraph, direction,
                    tab.TextLayout.PersianFont, tab.TextLayout.EnglishFont);
            }

            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        if (generation == tab.DirectionApplyGeneration && _tabs.Contains(tab))
            RefreshEditorPresentation(tab);
    }

    private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
        => SetDirectionMode(
            _active?.TextLayout.Mode == TextDirectionMode.Ltr
                ? TextDirectionMode.Normal
                : TextDirectionMode.Ltr);

    private void AlignRightButton_Click(object sender, RoutedEventArgs e)
        => SetDirectionMode(
            _active?.TextLayout.Mode == TextDirectionMode.Rtl
                ? TextDirectionMode.Normal
                : TextDirectionMode.Rtl);

    // ==================== rich text formatting toolbar ====================

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
        EditingCommands.ToggleBold.Execute(null, _active.Editor);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        EditingCommands.ToggleItalic.Execute(null, _active.Editor);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        EditingCommands.ToggleUnderline.Execute(null, _active.Editor);
        MarkActiveDirty();
        UpdateFormatToolbarState(_active);
    }

    private void BulletListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        EditingCommands.ToggleBullets.Execute(null, _active.Editor);
        MarkActiveDirty();
    }

    private void NumberListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        EditingCommands.ToggleNumbering.Execute(null, _active.Editor);
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
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
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
        => ShowColorPopup(sender, brush => ApplySelectionProperty(TextElement.ForegroundProperty, brush));

    private void HighlightButton_Click(object sender, RoutedEventArgs e)
        => ShowColorPopup(sender, brush => ApplySelectionProperty(TextElement.BackgroundProperty, brush));

    private void ShowColorPopup(object? sender, Action<SolidColorBrush> apply)
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

    /// <summary>Max image display size follows the editor width:
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
            var playing = false;
            var playButton = new Button
            {
                Content = "▶  " + System.IO.Path.GetFileName(path),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand
            };
            playButton.Click += (_, _) =>
            {
                if (playing)
                    media.Pause();
                else
                    media.Play();
                playing = !playing;
            };
            var panel = new StackPanel { Tag = path, Margin = new Thickness(2) };
            panel.Children.Add(media);
            panel.Children.Add(playButton);

            InsertStandaloneInline(panel);
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
                FontSizeBox.Text = size.ToString("0.#");
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    // ==================== status / title / menu state ====================

    private void UpdateWindowTitle()
        => Title = _active is null
            ? Get("AppName")
            : $"{(_active.IsDirty ? "*" : string.Empty)}{_active.FileName} - {Get("AppName")}";

    private void OnLanguageChanged()
    {
        foreach (var tab in _tabs)
            tab.RefreshTitle();
        UpdateWindowTitle();
        UpdateStatus();
        ApplySidebarLayout();
        RefreshOutline();
        if (FindPanel.Visibility == Visibility.Visible)
            RefreshFindHighlights();
    }

    private void UpdateStatus() => UpdateStatusFor(_active);

    private void UpdateStatusFor(DocumentTab? tab)
    {
        if (tab is null)
        {
            LnText.Text = Get("Line", 1);
            ColText.Text = Get("Column", 1);
            LinesText.Text = Get("OneLine");
            CharsText.Text = Get("Characters", 0);
            EncodingText.Text = "UTF-8";
            return;
        }

        var editor = tab.Editor;
        var text = editor.GetText();
        var chars = text.Replace("\r\n", "\n").Length;
        var lines = RichTextHelper.CountLines(editor);

        LinesText.Text = lines == 1 ? Get("OneLine") : Get("Lines", lines);
        CharsText.Text = chars == 1 ? Get("OneCharacter") : Get("Characters", chars);
        EncodingText.Text = tab.EncodingLabel;

        if (_settings.WordWrap)
        {
            LnColPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            LnColPanel.Visibility = Visibility.Visible;
            var (line, column) = RichTextHelper.GetLineColumn(editor);
            LnText.Text = Get("Line", line);
            ColText.Text = Get("Column", column);
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
        LineNumbersMenuItem.IsChecked = _settings.ShowLineNumbers;
        RestoreSessionMenuItem.IsChecked = _settings.RestoreSession;
        SidebarMenuItem.IsChecked = _sidebarMode != SidebarMode.Closed;
        RefreshRecentMenuHeader();
    }

    // ==================== recent files ====================

    private void AddRecentFile(string path)
    {
        _settings.RecentFiles.Remove(path);
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        RefreshRecentMenuHeader();
    }

    private void RefreshRecentMenuHeader()
    {
        if (RecentMenuItem.Items.Count == 1 && RecentMenuItem.Items[0] is MenuItem only && !only.IsEnabled)
            return; // "no recent files" placeholder still shown
        // header stays "Recent" once populated; the placeholder text only shows when empty
    }

    private void RecentMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecentMenuItem.Items.Clear();
        if (_settings.RecentFiles.Count == 0)
        {
            RecentMenuItem.Items.Add(new MenuItem
            {
                Header = Get("NoRecentFiles"),
                IsEnabled = false
            });
            return;
        }

        foreach (var path in _settings.RecentFiles.ToList())
        {
            var captured = path;
            var item = new MenuItem
            {
                Header = System.IO.Path.GetFileName(captured),
                ToolTip = captured
            };
            item.SetResourceReference(FlowDirectionProperty, "Ui.Direction");
            item.Click += (_, _) => OpenFile(captured);
            RecentMenuItem.Items.Add(item);
        }

        RecentMenuItem.Items.Add(new Separator());
        var clear = new MenuItem { Header = Get("ClearRecent") };
        clear.SetResourceReference(FlowDirectionProperty, "Ui.Direction");
        clear.Click += (_, _) =>
        {
            _settings.RecentFiles.Clear();
            RecentMenuItem.Items.Clear();
            RecentMenuItem.Items.Add(new MenuItem { Header = Get("NoRecentFiles"), IsEnabled = false });
        };
        RecentMenuItem.Items.Add(clear);
    }

    // ==================== startup / persistence ====================

    private void ApplyStartupSettings(AppSettings settings)
    {
        ApplyTypographySettings();
        LocalizationService.Apply(settings.InterfaceLanguage);
        _zoomPercent = Math.Clamp(settings.ZoomPercent, 10, 500);
        UpdateZoomLabels();
        WordWrapMenuItem.IsChecked = settings.WordWrap;
        StatusBarMenuItem.IsChecked = settings.ShowStatusBar;
        StatusBar.Visibility = settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        _sidebarMode = Enum.IsDefined(typeof(SidebarMode), settings.SidebarState)
            ? (SidebarMode)settings.SidebarState
            : SidebarMode.Pinned;
        _sidebarWidth = Math.Clamp(settings.SidebarWidth, 180, 700);
        ApplySidebarLayout();
        if (!string.IsNullOrWhiteSpace(settings.OpenedFolder) &&
            Directory.Exists(settings.OpenedFolder))
        {
            OpenWorkspaceFolder(settings.OpenedFolder);
        }
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
        _settings.ShowStatusBar = StatusBarMenuItem.IsChecked;
        _settings.ShowLineNumbers = LineNumbersMenuItem.IsChecked;
        _settings.RestoreSession = RestoreSessionMenuItem.IsChecked;
        _settings.SidebarState = (int)_sidebarMode;
        _settings.SidebarWidth = _sidebarWidth;
        _settings.OpenedFolder = _workspacePath;
        _settings.ZoomPercent = _zoomPercent;
        _settings.SessionFiles = _tabs
            .Where(t => t.FilePath is not null)
            .Select(t => t.FilePath!)
            .ToList();

        if (WindowState == WindowState.Normal)
        {
            _settings.Window = new WindowBounds
            {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
                Maximized = false
            };
        }
        else
        {
            var restore = RestoreBounds;
            _settings.Window = new WindowBounds
            {
                Left = restore.Left,
                Top = restore.Top,
                Width = restore.Width,
                Height = restore.Height,
                Maximized = true
            };
        }

        SettingsService.Save(_settings);

        // Full session (with drafts for unsaved content) is written on every
        // regular shutdown; on crash the last maintenance-timer snapshot helps.
        SaveSessionSnapshot();
    }

    /// <summary>Writes the complete tab state (drafts included) for the next startup.</summary>
    private void SaveSessionSnapshot()
    {
        if (!_settings.RestoreSession)
        {
            SessionService.Clear();
            return;
        }

        var state = new SessionState();
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            string? draft = null;
            if (tab.IsDirty || tab.FilePath is null)
                draft = SessionService.WriteDraft(tab.Editor.GetText());

            state.Tabs.Add(new SessionTabState
            {
                FilePath = tab.FilePath,
                DraftFile = draft,
                EncodingLabel = tab.EncodingLabel,
                IsRightToLeft = tab.TextLayout.Mode == TextDirectionMode.Rtl,
                DirectionMode = (int)tab.TextLayout.Mode,
                ScrollVertical = tab.ScrollVertical,
                ScrollHorizontal = tab.ScrollHorizontal
            });
            if (ReferenceEquals(_active, tab))
                state.ActiveIndex = i;
        }
        SessionService.Save(state);
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
            ApplyTypographySettings();
            foreach (var tab in _tabs)
                ClearTabHighlights(tab);

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
        else if (msg == WM_INPUTLANGCHANGE)
        {
            // Alt+Shift does not always raise InputLanguageManager.InputLanguageChanged,
            // so read the new keyboard layout straight from the Windows message.
            var languageId = (int)(lParam.ToInt64() & 0xFFFF);
            CultureInfo? culture = null;
            try { culture = CultureInfo.GetCultureInfo(languageId); }
            catch (CultureNotFoundException) { }
            Dispatcher.BeginInvoke(() => ApplyInputLanguageDirection(culture), DispatcherPriority.Input);
        }
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

    private void FileAssociations_Executed(object sender, ExecutedRoutedEventArgs e)
        => FileAssociationDialog.Show(this);

    private void About_Executed(object sender, ExecutedRoutedEventArgs e)
        => AboutDialog.Show(this);

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
            MessageDialog.ShowInfo(this, Get("AppName"), Get("GoToUnavailable"));
            return;
        }

        var totalLines = RichTextHelper.CountLines(tab.Editor);
        var input = InputDialog.Show(this, Get("GoTo"), Get("LineNumber", totalLines), "1", value =>
        {
            if (!int.TryParse(value, out var line) || line < 1 || line > totalLines)
                return Get("InvalidLine", totalLines);
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

    private void FontSettings_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!FontSettingsDialog.Show(this, _settings))
            return;

        ApplyTypographySettings();
        LocalizationService.Apply(_settings.InterfaceLanguage);
        SettingsService.Save(_settings);
        FocusActiveEditor();
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

    // ==================== phase 2: line numbers / session / text tools ====================

    private void LineNumbers_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _settings.ShowLineNumbers = LineNumbersMenuItem.IsChecked;
        foreach (var tab in _tabs)
        {
            tab.Gutter.Visibility = _settings.ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;
            tab.RightGutter.Visibility = _settings.ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.ShowLineNumbers)
                ScheduleGutterUpdate(tab);
            else
            {
                tab.Gutter.Children.Clear();
                tab.RightGutter.Children.Clear();
            }
        }
    }

    private void RestoreSession_Executed(object sender, ExecutedRoutedEventArgs e)
        => _settings.RestoreSession = RestoreSessionMenuItem.IsChecked;

    private void ReplaceSelectionOrAll(DocumentTab tab, Func<string, string> transform)
    {
        var selection = new TextRange(tab.Editor.Selection.Start, tab.Editor.Selection.End);
        tab.Editor.BeginChange();
        if (!selection.IsEmpty)
            selection.Text = transform(selection.Text.Replace("\r\n", "\n"));
        else
            new TextRange(tab.Editor.Document.ContentStart, tab.Editor.Document.ContentEnd).Text =
                transform(tab.Editor.GetText().Replace("\r\n", "\n"));
        tab.Editor.EndChange();
    }

    private void UpperCase_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is not null)
            ReplaceSelectionOrAll(_active, static text => text.ToUpperInvariant());
    }

    private void LowerCase_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is not null)
            ReplaceSelectionOrAll(_active, static text => text.ToLowerInvariant());
    }

    private void TitleCase_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is null)
            return;
        ReplaceSelectionOrAll(_active, text =>
            Regex.Replace(text, @"\b\p{L}", match => match.Value.ToUpperInvariant()));
    }

    private void TrimTrailing_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is null)
            return;
        ReplaceSelectionOrAll(_active, static text => string.Join(
            "\n", text.Split('\n').Select(line => line.TrimEnd())));
    }

    private void SortLinesAsc_Executed(object sender, ExecutedRoutedEventArgs e)
        => SortLines(_active, descending: false);

    private void SortLinesDesc_Executed(object sender, ExecutedRoutedEventArgs e)
        => SortLines(_active, descending: true);

    private void SortLines(DocumentTab? tab, bool descending)
    {
        if (tab is null)
            return;
        ReplaceSelectionOrAll(tab, text =>
        {
            var lines = text.Split('\n');
            var ordered = descending
                ? lines.OrderByDescending(line => line, StringComparer.CurrentCulture).ToArray()
                : lines.OrderBy(line => line, StringComparer.CurrentCulture).ToArray();
            return string.Join("\n", ordered);
        });
    }

    private void RemoveDuplicates_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_active is null)
            return;
        ReplaceSelectionOrAll(_active, text =>
        {
            var seen = new HashSet<string>(StringComparer.CurrentCulture);
            var result = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                if (seen.Add(line))
                    result.Add(line);
            }
            return string.Join("\n", result);
        });
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
            var result = MessageDialog.Show(this, Get("AppName"), Get("SaveChanges", tab.FileName),
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
        InputLanguageManager.Current.InputLanguageChanged -= InputLanguage_Changed;
        LocalizationService.Changed -= OnLanguageChanged;
        PersistSettings();
    }
}
