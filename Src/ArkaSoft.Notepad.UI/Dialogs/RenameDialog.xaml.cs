using System.IO;
using System.Windows;
using System.Windows.Input;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

/// <summary>Rename/change-extension dialog: name and extension fields separate.</summary>
public partial class RenameDialog : Window
{
    private readonly bool _isFolder;

    private RenameDialog(string title, string label, string fileName, bool isFolder)
    {
        InitializeComponent();
        _isFolder = isFolder;
        DialogTitle.Text = title;
        DialogLabel.Text = label;

        var ext = Path.GetExtension(fileName);
        var name = fileName[..^ext.Length];
        NameBox.Text = name;
        ExtBox.Text = ext.TrimStart('.');
        ExtRowVisible();

        NameBox.SelectAll();
        NameBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    private void ExtRowVisible()
    {
        var isHidden = _isFolder;
        ExtBox.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
        DotText.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Returns (nameWithoutDot, extensionWithoutDot), or null when cancelled.</summary>
    public static (string Name, string Extension)? Show(Window owner, string title, string label,
        string fileName, bool isFolder)
    {
        var dialog = new RenameDialog(title, label, fileName, isFolder) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return null;
        return (dialog.NameBox.Text.Trim(), dialog.ExtBox.Text.Trim().TrimStart('.'));
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var ext = ExtBox.Text.Trim().TrimStart('.');
        var invalid = new string(Path.GetInvalidFileNameChars());

        if (name.Length == 0)
        {
            Fail(LocalizationService.Get("EnterFileName"));
            return;
        }
        if (name.IndexOfAny(invalid.ToCharArray()) >= 0)
        {
            Fail(LocalizationService.Get("InvalidFileName", invalid));
            return;
        }
        if (!_isFolder)
        {
            if (ext.Length == 0)
            {
                Fail(LocalizationService.Get("EnterFileName"));
                return;
            }
            if (ext.IndexOfAny(invalid.ToCharArray()) >= 0 || ext.Contains('.'))
            {
                Fail(LocalizationService.Get("InvalidFileName", invalid));
                return;
            }
        }

        DialogResult = true;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
