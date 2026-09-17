using System.Windows;
using System.Windows.Controls;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

public enum MessageDialogResult
{
    Cancel = 0,
    Primary = 1,
    Secondary = 2,
    Third = 3
}

public sealed record DialogButton(string Label, MessageDialogResult Result, bool IsAccent = false);

public sealed class MessageDialogButtonSet
{
    public static readonly DialogButton[] Ok = { new("OK", MessageDialogResult.Primary, true) };
    public static readonly DialogButton[] OkCancel =
    {
        new("Cancel", MessageDialogResult.Cancel),
        new("OK", MessageDialogResult.Primary, true)
    };
    public static readonly DialogButton[] SaveDiscardCancel =
    {
        new("Don't save", MessageDialogResult.Secondary),
        new("Cancel", MessageDialogResult.Cancel),
        new("Save", MessageDialogResult.Primary, true)
    };
    public static readonly DialogButton[] YesNo =
    {
        new("No", MessageDialogResult.Secondary),
        new("Yes", MessageDialogResult.Primary, true)
    };
}

public partial class MessageDialog : Window
{
    private MessageDialogResult _result = MessageDialogResult.Cancel;

    private MessageDialog(string title, string message, DialogButton[] buttons)
    {
        InitializeComponent();
        Title = title;
        DialogTitle.Text = title;
        DialogMessage.Text = message;

        foreach (var button in buttons)
        {
            var btn = new Button
            {
                Content = LocalizationService.Get(button.Label == "Don't save" ? "DontSave" : button.Label),
                MinWidth = 110,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = button.Result == MessageDialogResult.Cancel,
                IsDefault = button.IsAccent
            };
            if (button.IsAccent)
                btn.Style = (Style)FindResource("Btn.Accent");
            var captured = button.Result;
            btn.Click += (_, _) =>
            {
                _result = captured;
                DialogResult = true;
            };
            ButtonPanel.Children.Add(btn);
        }

        if (ButtonPanel.Children.Count > 0 && buttons[^1].IsAccent)
            Loaded += (_, _) => ((Button)ButtonPanel.Children[^1]).Focus();
    }

    public static MessageDialogResult Show(Window owner, string title, string message, DialogButton[] buttons)
    {
        var dialog = new MessageDialog(title, message, buttons) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }

    public static void ShowInfo(Window owner, string title, string message)
        => Show(owner, title, message, MessageDialogButtonSet.Ok);

    private void Window_DragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            _result = MessageDialogResult.Cancel;
            DialogResult = false;
            e.Handled = true;
        }
    }
}
