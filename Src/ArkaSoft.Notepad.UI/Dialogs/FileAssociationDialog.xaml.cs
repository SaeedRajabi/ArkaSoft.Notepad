using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

public partial class FileAssociationDialog : Window
{
    private FileAssociationDialog()
    {
        InitializeComponent();
        DialogTitle.Text = LocalizationService.Get("FileAssocTitle");
        DialogHelp.Text = LocalizationService.Get("FileAssocHelp");
        HintText.Text = LocalizationService.Get("FileAssocHint");
        SettingsButton.Content = LocalizationService.Get("OpenDefaultApps");
        ApplyButton.Content = LocalizationService.Get("Apply");

        foreach (var ext in FileAssociationService.OfferedExtensions)
        {
            var item = new CheckBox
            {
                Content = "*" + ext + (FileAssociationService.IsDefault(ext)
                    ? "  (" + LocalizationService.Get("IsDefaultNow") + ")"
                    : string.Empty),
                Margin = new Thickness(0, 4, 0, 4),
                Tag = ext
            };
            item.SetResourceReference(FlowDirectionProperty, "Ui.Direction");
            ExtensionList.Children.Add(item);
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    public static void Show(Window owner)
    {
        var dialog = new FileAssociationDialog { Owner = owner };
        dialog.ShowDialog();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        FileAssociationService.EnsureRegistered();
        foreach (var item in ExtensionList.Children.OfType<CheckBox>())
        {
            if (item.Tag is string ext)
                FileAssociationService.SetAssociation(ext, item.IsChecked == true);
        }
        MessageDialog.ShowInfo(this, LocalizationService.Get("AppName"),
            LocalizationService.Get("AssocApplied"));
        DialogResult = true;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => FileAssociationService.OpenWindowsDefaultAppsSettings();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
