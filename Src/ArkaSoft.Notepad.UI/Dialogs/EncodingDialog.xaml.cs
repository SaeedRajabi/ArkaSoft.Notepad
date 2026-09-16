using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

public partial class EncodingDialog : Window
{
    private EncodingDialog(string currentLabel)
    {
        InitializeComponent();
        foreach (var option in FileService.EncodingChoices)
            EncodingCombo.Items.Add(option);
        EncodingCombo.SelectedItem = FileService.FindOption(currentLabel) ?? FileService.EncodingChoices[0];
        EncodingCombo.SelectedIndex = Math.Max(0, EncodingCombo.SelectedIndex);
        Loaded += (_, _) => EncodingCombo.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    /// <summary>Returns the chosen encoding label, or null when cancelled.</summary>
    public static string? Show(Window owner, string currentLabel)
    {
        var dialog = new EncodingDialog(currentLabel) { Owner = owner };
        return dialog.ShowDialog() == true
            ? ((EncodingOption)dialog.EncodingCombo.SelectedItem).Label
            : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
