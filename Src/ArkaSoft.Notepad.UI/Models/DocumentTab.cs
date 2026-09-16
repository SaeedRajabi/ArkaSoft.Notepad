using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ArkaSoft.Notepad.UI.Models;

public sealed class DocumentTab : INotifyPropertyChanged
{
    private string? _filePath;
    private bool _isDirty;
    private string _encodingLabel;
    private bool _isRightToLeft;
    private double _scrollVertical;
    private double _scrollHorizontal;
    private readonly List<(int Start, int Length)> _highlights = new();

    public DocumentTab(RichTextBox editor, Encoding encoding, string encodingLabel, string? filePath)
    {
        Editor = editor;
        Encoding = encoding;
        _encodingLabel = encodingLabel;
        _filePath = filePath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RichTextBox Editor { get; }

    public Encoding Encoding { get; set; }

    public IReadOnlyList<(int Start, int Length)> Highlights => _highlights;

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string FileName => FilePath is null
        ? "Untitled"
        : Path.GetFileName(FilePath);

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string Title => IsDirty ? $"● {FileName}" : FileName;

    public string EncodingLabel
    {
        get => _encodingLabel;
        set
        {
            if (_encodingLabel != value)
            {
                _encodingLabel = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRightToLeft
    {
        get => _isRightToLeft;
        set
        {
            if (_isRightToLeft != value)
            {
                _isRightToLeft = value;
                OnPropertyChanged();
            }
        }
    }

    public double ScrollVertical
    {
        get => _scrollVertical;
        set => _scrollVertical = value;
    }

    public double ScrollHorizontal
    {
        get => _scrollHorizontal;
        set => _scrollHorizontal = value;
    }

    public void SetHighlights(IEnumerable<(int Start, int Length)> ranges)
    {
        _highlights.Clear();
        _highlights.AddRange(ranges);
    }

    public void ClearHighlights() => _highlights.Clear();

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
