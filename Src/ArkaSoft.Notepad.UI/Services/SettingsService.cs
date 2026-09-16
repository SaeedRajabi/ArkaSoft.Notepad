using System.IO;
using System.Text.Json;

namespace ArkaSoft.Notepad.UI.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private static string GetSettingsDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ArkaSoft.Notepad");

    private static string GetSettingsPath()
        => Path.Combine(GetSettingsDirectory(), "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // corrupted settings file: fall back to defaults
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(GetSettingsDirectory());
            File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // best effort persistence; never crash the app for settings IO
        }
    }

    public static void LogError(string message)
    {
        try
        {
            Directory.CreateDirectory(GetSettingsDirectory());
            File.AppendAllText(
                Path.Combine(GetSettingsDirectory(), "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
