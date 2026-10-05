using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArkaSoft.Notepad.UI.Services;

/// <summary>
/// Registers the app for file associations the way Notepad++ does: a per-user
/// ProgID whose DefaultIcon points at the app icon, "Open with" listings, and
/// optional default-editor registration for chosen extensions. Everything is
/// written under HKCU, so no elevation is required.
/// </summary>
public static class FileAssociationService
{
    public const string ProgId = "ArkaSoft.Notepad.Document";
    private const string AppName = "ArkaSoft Notepad";
    private const string AppDescription = "A lightweight Windows 11 style text editor by ArkaSoft";

    public static readonly string[] OfferedExtensions =
    [
        ".txt", ".log", ".md", ".ini", ".cfg", ".csv"
    ];

    public static string ExePath =>
        Environment.ProcessPath
            ?? AppContext.BaseDirectory + "ArkaSoft.Notepad.exe";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    /// <summary>Idempotent per-user registration: ProgID (icon + open command),
    /// the Applications entry, and Default-Apps capabilities. Safe to call on
    /// every startup so the paths stay current after rebuilds or moves.</summary>
    public static void EnsureRegistered()
    {
        try
        {
            var exe = ExePath;
            var command = $"\"{exe}\" \"%1\"";
            var iconRef = $"\"{exe}\",0";

            using (var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                progId.SetValue(null, AppName + " Text Document");
                progId.SetValue("FriendlyTypeName", AppName + " Text Document");
                using (var defaultIcon = progId.CreateSubKey("DefaultIcon"))
                    defaultIcon.SetValue(null, iconRef);
                using (var open = progId.CreateSubKey(@"shell\open\command"))
                    open.SetValue(null, command);
            }

            using (var appKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\ArkaSoft.Notepad.exe"))
            {
                using (var open = appKey.CreateSubKey(@"shell\open\command"))
                    open.SetValue(null, command);
                appKey.SetValue("FriendlyAppName", AppName);
            }

            using (var caps = Registry.CurrentUser.CreateSubKey(@"Software\ArkaSoft.Notepad\Capabilities"))
            {
                caps.SetValue("ApplicationName", AppName);
                caps.SetValue("ApplicationIcon", iconRef);
                caps.SetValue("ApplicationDescription", AppDescription);
                using (var fileAssoc = caps.CreateSubKey("FileAssociations"))
                {
                    foreach (var ext in OfferedExtensions)
                        fileAssoc.SetValue(ext, ProgId);
                }
            }

            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                registered.SetValue(AppName, @"Software\ArkaSoft.Notepad\Capabilities");
        }
        catch
        {
            // registry writes are best effort; the editor still works unregistered
        }
    }

    /// <summary>True when this app is currently the default handler for the extension.</summary>
    public static bool IsDefault(string extension)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + extension);
            return string.Equals(key?.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Attaches ("Open with" entry, and optionally the default handler)
    /// or detaches the app for one extension. Always refreshes the shell.</summary>
    public static void SetAssociation(string extension, bool makeDefault)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extension))
            {
                if (makeDefault)
                {
                    key.SetValue(null, ProgId);
                    using var openWith = key.CreateSubKey("OpenWithProgids");
                    openWith.SetValue(ProgId, string.Empty);

                    // Explorer prefers the per-extension UserChoice; removing it
                    // lets the new default take effect immediately.
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\"
                            + extension + @"\UserChoice", throwOnMissingSubKey: false);
                    }
                    catch
                    {
                        // a locked UserChoice means the user picks the default in Settings
                    }
                }
                else
                {
                    using var openWith = key.OpenSubKey("OpenWithProgids", writable: true);
                    openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                    if (string.Equals(key.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase))
                        key.DeleteValue(null, throwOnMissingValue: false);
                }
            }
        }
        catch
        {
            // best effort
        }
        NotifyShell();
    }

    /// <summary>Tells Explorer the associations changed so file icons refresh.</summary>
    public static void NotifyShell()
        => SHChangeNotify(SHCNE_ASSOCCHANGED, 0x0000, IntPtr.Zero, IntPtr.Zero);

    public static void OpenWindowsDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            });
        }
        catch
        {
            // settings launch is best effort
        }
    }
}
