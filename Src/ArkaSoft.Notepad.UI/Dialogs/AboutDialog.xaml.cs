using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        AppNameText.Text = "ArkaSoft Notepad";
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = LocalizationService.Get("VersionLabel",
            $"{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}");
        DevByText.Text = LocalizationService.Get("DevelopedBy");
        TeamText.Text = LocalizationService.Get("DevTeam");
        CopyrightText.Text = LocalizationService.Get("Copyright", DateTime.Now.Year);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    public static void Show(Window owner)
        => new AboutDialog { Owner = owner }.ShowDialog();

    private void SiteLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://arkasoftware.ir",
                UseShellExecute = true
            });
        }
        catch
        {
            // browser launch is best effort
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
