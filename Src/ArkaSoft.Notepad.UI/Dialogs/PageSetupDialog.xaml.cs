using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

public partial class PageSetupDialog : Window
{
    private static readonly string[] PaperSizes = { "A4", "A5", "B5", "Letter", "Legal", "Executive" };

    private PageSetupDialog(PrintSettings settings)
    {
        InitializeComponent();
        foreach (var paper in PaperSizes)
            PaperCombo.Items.Add(paper);
        PaperCombo.SelectedItem = PaperSizes.Contains(settings.PaperName) ? settings.PaperName : "A4";

        PortraitRadio.IsChecked = !settings.Landscape;
        LandscapeRadio.IsChecked = settings.Landscape;

        LeftBox.Text = settings.MarginLeftMm.ToString("0.#");
        TopBox.Text = settings.MarginTopMm.ToString("0.#");
        RightBox.Text = settings.MarginRightMm.ToString("0.#");
        BottomBox.Text = settings.MarginBottomMm.ToString("0.#");

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    /// <summary>Edits a copy of the settings; returns true when the user accepted valid values.</summary>
    public static bool Show(Window owner, PrintSettings settings)
    {
        var dialog = new PageSetupDialog(settings) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return false;

        settings.PaperName = (string)dialog.PaperCombo.SelectedItem;
        settings.Landscape = dialog.LandscapeRadio.IsChecked == true;
        settings.MarginLeftMm = double.Parse(dialog.LeftBox.Text);
        settings.MarginTopMm = double.Parse(dialog.TopBox.Text);
        settings.MarginRightMm = double.Parse(dialog.RightBox.Text);
        settings.MarginBottomMm = double.Parse(dialog.BottomBox.Text);
        return true;
    }

    private void Orientation_Changed(object sender, RoutedEventArgs e)
    {
    }

    private void NumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !double.TryParse(e.Text, out _);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var boxes = new[] { LeftBox, TopBox, RightBox, BottomBox };
        foreach (var box in boxes)
        {
            if (!double.TryParse(box.Text, out var value) || value < 0 || value > 50)
            {
                ErrorText.Text = "Margins must be numbers between 0 and 50 millimeters.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
