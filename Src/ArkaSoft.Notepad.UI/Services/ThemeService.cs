using System.Windows;
using Microsoft.Win32;

namespace ArkaSoft.Notepad.UI.Services;

public static class ThemeService
{
    private const string PaletteLightUri = "Themes/Palette.Light.xaml";
    private const string PaletteDarkUri = "Themes/Palette.Dark.xaml";

    /// <summary>Raised whenever the effective (resolved) theme changes,
    /// including when the Windows system theme changes.</summary>
    public static event Action? EffectiveThemeChanged;

    public static AppTheme CurrentSetting { get; private set; } = AppTheme.System;
    public static bool EffectiveIsDark { get; private set; }

    public static void Apply(AppTheme setting)
    {
        CurrentSetting = setting;
        ApplyResolved(setting == AppTheme.System
            ? (SystemUsesLightTheme() ? AppTheme.Light : AppTheme.Dark)
            : setting);
    }

    /// <summary>Re-evaluates the system theme when Windows sends WM_SETTINGCHANGE
    /// (e.g. user switches dark mode) and re-applies if we are in System mode.</summary>
    public static void OnSystemSettingChanged()
    {
        if (CurrentSetting != AppTheme.System)
            return;
        var dark = !SystemUsesLightTheme();
        if (dark != EffectiveIsDark)
            ApplyResolved(dark ? AppTheme.Dark : AppTheme.Light);
    }

    private static void ApplyResolved(AppTheme resolved)
    {
        var dark = resolved == AppTheme.Dark;
        EffectiveIsDark = dark;

        var app = Application.Current;
        if (app is null)
            return;

        var uri = new Uri(dark ? PaletteDarkUri : PaletteLightUri, UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = uri };

        if (app.Resources.MergedDictionaries.Count == 0)
            app.Resources.MergedDictionaries.Add(dictionary);
        else
            app.Resources.MergedDictionaries[0] = dictionary;

        EffectiveThemeChanged?.Invoke();
    }

    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 1;
        }
        catch
        {
            // registry access denied or missing: assume light
        }
        return true;
    }
}
