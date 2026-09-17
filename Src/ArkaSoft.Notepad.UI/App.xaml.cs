using System.IO;
using System.Windows;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsService.Load();
        ThemeService.Apply((AppTheme)settings.Theme);
        TypographyService.Apply(settings);
        LocalizationService.Apply(settings.InterfaceLanguage);

        DispatcherUnhandledException += (_, args) =>
        {
            SettingsService.LogError(args.Exception.ToString());
            MessageBox.Show(
                LocalizationService.Get("UnexpectedError", args.Exception.Message),
                LocalizationService.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var argFiles = e.Args
            .Where(a => !a.StartsWith("-", StringComparison.Ordinal))
            .ToList();

        if (argFiles.Count > 0)
        {
            new MainWindow(settings, argFiles).Show();
        }
        else if (settings.SessionFiles.Count > 0 && settings.SessionFiles.Any(File.Exists))
        {
            new MainWindow(settings, settings.SessionFiles).Show();
        }
        else
        {
            new MainWindow(settings).Show();
        }
    }
}
