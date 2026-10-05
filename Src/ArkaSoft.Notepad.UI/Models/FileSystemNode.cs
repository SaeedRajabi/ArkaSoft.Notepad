using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ArkaSoft.Notepad.UI.Models;

/// <summary>One entry of the Explorer sidebar's file tree.</summary>
public sealed class FileSystemNode : INotifyPropertyChanged
{
    private string _name;
    private bool _isExpanded;
    private bool _isPlaceholder;
    private bool _isEditing;
    private bool _nameError;

    private FileSystemNode(string name, string fullPath, bool isFolder)
    {
        _name = name;
        FullPath = fullPath;
        IsFolder = isFolder;
        Children = new ObservableCollection<FileSystemNode>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string FullPath { get; set; }

    public bool IsFolder { get; }

    /// <summary>True for the dummy child that makes a lazy folder show its expander.</summary>
    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        private set { _isPlaceholder = value; OnPropertyChanged(); }
    }

    /// <summary>True while the inline new-item editor is active for this node.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set { _isEditing = value; OnPropertyChanged(); }
    }

    /// <summary>True when the inline editor flagged the current name as invalid.</summary>
    public bool NameError
    {
        get => _nameError;
        set { _nameError = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>Real children have been read from disk at least once.</summary>
    public bool ChildrenLoaded { get; set; }

    /// <summary>Short badge label for the extension ("CS", "PY", …); empty = generic page glyph.</summary>
    public string IconLabel { get; set; } = string.Empty;

    public Brush IconBrush { get; set; } = Brushes.Gray;

    public Brush IconForeground { get; set; } = Brushes.White;

    public ObservableCollection<FileSystemNode> Children { get; }

    public static FileSystemNode CreateFile(string name, string fullPath)
        => new(name, fullPath, isFolder: false);

    public static FileSystemNode CreateFolder(string name, string fullPath)
        => new(name, fullPath, isFolder: true);

    public static FileSystemNode CreatePlaceholder()
        => new("…", string.Empty, isFolder: false) { IsPlaceholder = true };

    public static FileSystemNode CreateEditing(bool isFolder)
        => new(string.Empty, string.Empty, isFolder) { IsEditing = true };

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
