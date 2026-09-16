using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArkaSoft.Notepad.UI.Dialogs;

public partial class InputDialog : Window
{
    private readonly Func<string, string?>? _validator;

    private InputDialog(string title, string label, string initial, Func<string, string?>? validator)
    {
        InitializeComponent();
        Title = title;
        DialogTitle.Text = title;
        DialogLabel.Text = label;
        InputBox.Text = initial;
        _validator = validator;
        InputBox.SelectAll();
        Loaded += (_, _) => InputBox.Focus();
        InputBox.PreviewKeyDown += InputBox_PreviewKeyDown;
    }

    /// <summary>Returns the entered text, or null when cancelled or validation failed.</summary>
    public static string? Show(Window owner, string title, string label, string initial,
        Func<string, string?>? validator = null)
    {
        var dialog = new InputDialog(title, label, initial, validator) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.InputBox.Text : null;
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAccept();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TryAccept()
    {
        var error = _validator?.Invoke(InputBox.Text);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
