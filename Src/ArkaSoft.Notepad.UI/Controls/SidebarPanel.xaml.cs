using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArkaSoft.Notepad.UI.Helpers;
using ArkaSoft.Notepad.UI.Models;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Controls;

public enum SidebarNodeAction
{
    Open,
    NewFile,
    NewFolder,
    Rename,
    Delete,
    Reveal
}

/// <summary>
/// Explorer sidebar. Two tree modes share one TreeView (implicit templates):
/// document outline when no folder is open, file tree when a workspace folder
/// is open. Right-click offers VS Code-style file operations; the window
/// performs them and asks the panel to reload while preserving expansion.
/// </summary>
public partial class SidebarPanel : UserControl
{
    private FileSystemNode? _workspaceRoot;
    private readonly List<OutlineNode> _flat = new();

    public event Action? PinClick;
    public event Action? AutoClick;
    public event Action? CloseClick;
    public event Action? StripMouseEnter;
    public event Action? PanelMouseEnter;
    public event Action? PanelMouseLeave;
    public event Action<int>? LineClicked;
    public event Action? ToolbarOpenFolder;
    public event Action? ToolbarNewFile;
    public event Action? ToolbarNewFolder;
    public event Action? ToolbarRefresh;
    public event Action<FileSystemNode, SidebarNodeAction>? NodeAction;

    /// <summary>(parentPath, name, isFolder) — raised after inline-edit commit;
    /// MainWindow creates the item on disk and reloads the tree.</summary>
    public event Action<string, string, bool>? ItemCreateRequested;

    /// <summary>Pointer X relative to the window while the resize thumb is dragged.</summary>
    public event Action<double>? ResizePreview;

    /// <summary>The hold-open state changed (menu opened/closed, editing started/ended);
    /// the window re-evaluates auto-hide.</summary>
    public event Action? HoldChanged;

    /// <summary>True while a context menu or the inline editor is active:
    /// auto-hide must not close the sidebar during them.</summary>
    public bool HoldOpen => _menuOpen || _editingNode is not null;

    public SidebarPanel()
    {
        InitializeComponent();
        StripView.MouseEnter += (_, _) => StripMouseEnter?.Invoke();
        FullView.MouseEnter += (_, _) => PanelMouseEnter?.Invoke();
        FullView.MouseLeave += (_, _) => PanelMouseLeave?.Invoke();
        PinButton.Click += (_, _) => PinClick?.Invoke();
        AutoButton.Click += (_, _) => AutoClick?.Invoke();
        CloseButton.Click += (_, _) => CloseClick?.Invoke();
        OpenFolderButton.Click += (_, _) => ToolbarOpenFolder?.Invoke();
        NewFileButton.Click += (_, _) => ToolbarNewFile?.Invoke();
        NewFolderButton.Click += (_, _) => ToolbarNewFolder?.Invoke();
        RefreshButton.Click += (_, _) => ToolbarRefresh?.Invoke();

        OutlineTree.AddHandler(TreeViewItem.ExpandedEvent,
            new RoutedEventHandler(OnTreeItemExpanded));
        // the menu must be assigned BEFORE WPF decides to open it, otherwise the
        // first right-click finds no menu and only the second one works
        OutlineTree.PreviewMouseRightButtonDown += OutlineTree_RightButtonDown;
    }

    public static readonly DependencyProperty HasWorkspaceProperty =
        DependencyProperty.Register(nameof(HasWorkspace), typeof(bool), typeof(SidebarPanel),
            new PropertyMetadata(false));

    public bool HasWorkspace
    {
        get => (bool)GetValue(HasWorkspaceProperty);
        private set => SetValue(HasWorkspaceProperty, value);
    }

    public static readonly DependencyProperty ShowStripProperty =
        DependencyProperty.Register(nameof(ShowStrip), typeof(bool), typeof(SidebarPanel),
            new PropertyMetadata(false, OnShowStripChanged));

    public bool ShowStrip
    {
        get => (bool)GetValue(ShowStripProperty);
        set => SetValue(ShowStripProperty, value);
    }

    public FileSystemNode? SelectedFsNode => OutlineTree.SelectedItem as FileSystemNode;

    public FileSystemNode? RootNode => _workspaceRoot;

    /// <summary>Reads folder children on first expansion (set by MainWindow).</summary>
    public Func<FileSystemNode, IEnumerable<FileSystemNode>>? ChildrenProvider { get; set; }

    private static void OnShowStripChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (SidebarPanel)d;
        var strip = (bool)e.NewValue;
        panel.StripView.Visibility = strip ? Visibility.Visible : Visibility.Collapsed;
        panel.FullView.Visibility = strip ? Visibility.Collapsed : Visibility.Visible;
        if (strip)
            panel.StripBar.Fill = panel.TryFindResource("C.TextDisabled") as Brush ?? Brushes.Gray;
    }

    // ==================== outline mode ====================

    /// <summary>Switches to document-outline mode for the active tab.</summary>
    public void LoadOutline(string rootTitle, IReadOnlyList<OutlineNode> nodes)
    {
        if (_workspaceRoot is not null)
            return; // a workspace is open; outline stays hidden
        _flat.Clear();
        var root = new OutlineNode(rootTitle, 0) { EndLine = int.MaxValue };
        foreach (var node in nodes)
        {
            root.Children.Add(node);
            Flatten(node, _flat);
        }
        _flat.Add(root);
        OutlineTree.ItemsSource = new List<OutlineNode> { root };
    }

    public void SetActiveLine(int line)
    {
        if (_workspaceRoot is not null)
            return;
        OutlineNode? best = null;
        foreach (var node in _flat)
        {
            if (node.Line <= line && line < node.EndLine)
            {
                if (best is null || node.Line >= best.Line)
                    best = node;
            }
        }
        foreach (var node in _flat)
            node.IsActive = ReferenceEquals(node, best);
    }

    private void OutlineItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OutlineNode node })
        {
            SelectTreeItem(sender as DependencyObject);
            LineClicked?.Invoke(node.Line);
        }
    }

    private static void Flatten(OutlineNode node, List<OutlineNode> into)
    {
        into.Add(node);
        foreach (var child in node.Children)
            Flatten(child, into);
    }

    // ==================== workspace mode ====================

    public void ShowWorkspace(FileSystemNode root)
    {
        _workspaceRoot = root;
        HasWorkspace = true;
        _flat.Clear();
        OutlineTree.ItemsSource = new List<FileSystemNode> { root };
    }

    public void CloseWorkspace()
    {
        _workspaceRoot = null;
        HasWorkspace = false;
        OutlineTree.ItemsSource = null;
    }

    public HashSet<string> CollectExpandedPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_workspaceRoot is not null)
            CollectExpanded(_workspaceRoot, set);
        return set;
    }

    private static void CollectExpanded(FileSystemNode node, HashSet<string> into)
    {
        if (node.IsFolder && node.IsExpanded && !node.IsPlaceholder)
            into.Add(node.FullPath);
        foreach (var child in node.Children)
            CollectExpanded(child, into);
    }

    public void RestoreExpanded(HashSet<string> expandedPaths)
    {
        if (_workspaceRoot is null)
            return;
        RestoreExpanded(_workspaceRoot, expandedPaths);
    }

    private void RestoreExpanded(FileSystemNode node, HashSet<string> expandedPaths)
    {
        if (node.IsFolder && expandedPaths.Contains(node.FullPath))
        {
            EnsureChildrenLoaded(node);
            node.IsExpanded = true;
            foreach (var child in node.Children)
                RestoreExpanded(child, expandedPaths);
        }
    }

    /// <summary>Replaces the placeholder child with real disk content. Already-loaded
    /// folders keep their children so collapsing and re-expanding never empties them.</summary>
    public void EnsureChildrenLoaded(FileSystemNode folder)
    {
        if (!folder.IsFolder || folder.ChildrenLoaded)
            return;
        folder.Children.Clear();
        if (ChildrenProvider is null)
        {
            folder.ChildrenLoaded = true;
            return;
        }
        foreach (var child in ChildrenProvider(folder))
            folder.Children.Add(child);
        folder.ChildrenLoaded = true;
    }

    private void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileSystemNode { IsFolder: true } node })
            EnsureChildrenLoaded(node);
    }

    // ==================== inline new-item editing (VS Code style) ====================

    private FileSystemNode? _editingNode;
    private FileSystemNode? _editingParent;
    private TextBox? _editBox;
    private bool _menuOpen;
    private bool _resizing;

    /// <summary>Resolves the folder node a new item should be created under:
    /// the node itself when it is a folder, otherwise its parent, else the root.</summary>
    public FileSystemNode? ResolveParentFor(FileSystemNode node)
    {
        if (_workspaceRoot is null)
            return null;
        if (node.IsFolder)
            return node;
        return FindParentNode(_workspaceRoot, node) ?? _workspaceRoot;
    }

    private static FileSystemNode? FindParentNode(FileSystemNode current, FileSystemNode target)
    {
        foreach (var child in current.Children)
        {
            if (ReferenceEquals(child, target))
                return current;
            var found = FindParentNode(child, target);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>Inserts an inline editor row into the tree instead of showing a dialog.</summary>
    public void BeginCreateItem(FileSystemNode parentFolder, bool isFolder)
    {
        if (_workspaceRoot is null || parentFolder.IsPlaceholder)
            return;
        EnsureChildrenLoaded(parentFolder);
        CancelNewEditing();
        parentFolder.IsExpanded = true;
        _editingNode = FileSystemNode.CreateEditing(isFolder);
        _editingParent = parentFolder;
        parentFolder.Children.Insert(0, _editingNode);
        HoldChanged?.Invoke();
    }

    private void CancelNewEditing()
    {
        if (_editingParent is not null && _editingNode is not null)
            _editingParent.Children.Remove(_editingNode);
        _editingNode = null;
        _editingParent = null;
        _editBox = null;
        HoldChanged?.Invoke();
    }

    private void CommitNewEditing()
    {
        if (_editingNode is null || _editingParent is null)
            return;
        var name = _editingNode.Name.Trim();
        if (name.Length == 0)
        {
            CancelNewEditing();
            return;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".", StringComparison.Ordinal))
        {
            MarkNameError();
            return;
        }
        bool duplicate = _editingParent.Children
            .Where(child => !ReferenceEquals(child, _editingNode) && !child.IsPlaceholder)
            .Any(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            MarkNameError();
            return;
        }

        var parentPath = _editingParent.FullPath;
        var isFolder = _editingNode.IsFolder;
        _editingNode = null;
        _editingParent = null;
        _editBox = null;
        HoldChanged?.Invoke();
        ItemCreateRequested?.Invoke(parentPath, name, isFolder);
    }

    private void MarkNameError()
    {
        if (_editingNode is not null)
            _editingNode.NameError = true;
        // re-take focus so the user can fix the name; LostFocus re-commits
        if (_editBox is not null)
        {
            _editBox.Focus();
            _editBox.SelectAll();
        }
    }

    private void EditBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            _editBox = box;
            box.Focus();
            box.SelectAll();
        }
    }

    private void EditBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editingNode is not null && _editingNode.NameError)
            _editingNode.NameError = false;
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitNewEditing();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelNewEditing();
        }
    }

    private void EditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // clicking elsewhere commits, like VS Code
        if (_editingNode is not null)
            CommitNewEditing();
    }

    private void FsItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileSystemNode node })
        {
            SelectTreeItem(sender as DependencyObject);
            if (node.IsFolder)
            {
                // folders toggle on single click
                if (e.ClickCount == 1)
                    node.IsExpanded = !node.IsExpanded;
            }
            else if (e.ClickCount == 2)
            {
                // files open on double click, like VS Code and Explorer
                NodeAction?.Invoke(node, SidebarNodeAction.Open);
            }
        }
    }

    // ==================== context menu ====================

    /// <summary>Highlights the clicked item so selection always follows the mouse,
    /// for left-click and right-click alike (WPF does not select on right-click).</summary>
    private void SelectTreeItem(DependencyObject? source)
    {
        if (FindAncestor<TreeViewItem>(source) is { } container)
        {
            container.IsSelected = true;
            container.Focus();
        }
    }

    private void OutlineTree_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var node = container?.DataContext as FileSystemNode;

        if (node is null || node.IsPlaceholder)
        {
            // empty area of the tree: root-level actions when a workspace is open
            OutlineTree.ContextMenu = _workspaceRoot is not null
                ? BuildContextMenu(_workspaceRoot, rootMenu: true)
                : null;
            return;
        }

        // the right-clicked item becomes the selection, and the menu targets it
        SelectTreeItem(container);
        if (container is not null)
            container.ContextMenu = BuildContextMenu(node, rootMenu: false);
        OutlineTree.ContextMenu = null;
    }

    private ContextMenu BuildContextMenu(FileSystemNode node, bool rootMenu)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            HoldChanged?.Invoke();
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            HoldChanged?.Invoke();
        };

        MenuItem Mk(string key, SidebarNodeAction action, string glyph)
        {
            var item = new MenuItem { Header = LocalizationService.Get(key) };
            item.SetResourceReference(FlowDirectionProperty, "Ui.Direction");
            item.Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("F.Icons"),
                FontSize = 13,
                Foreground = (Brush)FindResource("C.Text2")
            };
            item.Click += (_, _) => NodeAction?.Invoke(node, action);
            menu.Items.Add(item);
            return item;
        }

        if (!rootMenu)
        {
            if (!node.IsFolder)
                Mk("Open", SidebarNodeAction.Open, "\uE7C3");
            Mk("NewFile", SidebarNodeAction.NewFile, "\uE7C3");
            Mk("NewFolder", SidebarNodeAction.NewFolder, "\uE8F4");
            menu.Items.Add(new Separator());
            Mk("Rename", SidebarNodeAction.Rename, "\uE8AC");
            Mk("DeleteItem", SidebarNodeAction.Delete, "\uE74D");
            Mk("RevealInExplorer", SidebarNodeAction.Reveal, "\uE838");
        }
        else
        {
            Mk("NewFile", SidebarNodeAction.NewFile, "\uE7C3");
            Mk("NewFolder", SidebarNodeAction.NewFolder, "\uE8F4");
            menu.Items.Add(new Separator());
            Mk("OpenFolder", SidebarNodeAction.Reveal, "\uE838");
            Mk("RefreshView", SidebarNodeAction.Rename, "\uE72C"); // replaced below
        }

        // for the root menu, "Reveal" and a dedicated refresh entry make sense:
        if (rootMenu)
        {
            // rebuild cleanly: last two entries were placeholders
            menu.Items.RemoveAt(menu.Items.Count - 1);
            var refresh = new MenuItem { Header = LocalizationService.Get("RefreshView") };
            refresh.SetResourceReference(FlowDirectionProperty, "Ui.Direction");
            refresh.Icon = new TextBlock
            {
                Text = "\uE72C",
                FontFamily = (FontFamily)FindResource("F.Icons"),
                FontSize = 13,
                Foreground = (Brush)FindResource("C.Text2")
            };
            refresh.Click += (_, _) => ToolbarRefresh?.Invoke();
            menu.Items.Add(refresh);
        }
        return menu;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T matched)
                return matched;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    // ==================== width resize thumb ====================

    private void ResizeThumb_Down(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        ResizeThumb.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeThumb_Move(object sender, MouseEventArgs e)
    {
        if (!_resizing)
            return;
        var win = Window.GetWindow(this);
        if (win is null)
            return;
        ResizePreview?.Invoke(Mouse.GetPosition(win).X);
        e.Handled = true;
    }

    private void ResizeThumb_Up(object sender, MouseButtonEventArgs e)
    {
        _resizing = false;
        ResizeThumb.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ResizeThumb_Enter(object sender, MouseEventArgs e)
        => ResizeLine.Fill = TryFindResource("C.Accent") as Brush ?? Brushes.DodgerBlue;

    private void ResizeThumb_Leave(object sender, MouseEventArgs e)
        => ResizeLine.Fill = Brushes.Transparent;
}
