using System.IO;
using System.Windows;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var argFiles = e.Args
            .Where(a => !a.StartsWith("-", StringComparison.Ordinal))
            .ToList();

        // Single instance: hand files off to the running window and exit.
        if (!SingleInstanceService.TryAcquire())
        {
            SingleInstanceService.ForwardToRunningInstance(argFiles);
            Shutdown();
            return;
        }

        var settings = SettingsService.Load();
        ThemeService.Apply((AppTheme)settings.Theme);
        TypographyService.Apply(settings);
        LocalizationService.Apply(settings.InterfaceLanguage);
        FileAssociationService.EnsureRegistered();

        DispatcherUnhandledException += (_, args) =>
        {
            SettingsService.LogError(args.Exception.ToString());
            MessageBox.Show(
                LocalizationService.Get("UnexpectedError", args.Exception.Message),
                LocalizationService.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        SessionState? session = null;
        if (argFiles.Count == 0 && settings.RestoreSession)
            session = SessionService.Load();
        else if (settings.RestoreSession)
            SessionService.Clear();

        MainWindow window;
        if (argFiles.Count > 0)
            window = new MainWindow(settings, argFiles);
        else if (session is not null)
            window = new MainWindow(settings, session: session);
        else
            window = new MainWindow(settings);
        window.Show();

        SingleInstanceService.StartServer(
            files => window.OpenFilesFromSecondInstance(files),
            () => window.BringToFront());
    }
}
