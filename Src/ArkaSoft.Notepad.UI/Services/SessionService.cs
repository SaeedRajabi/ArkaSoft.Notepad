using System.IO;
using System.Text.Json;

namespace ArkaSoft.Notepad.UI.Services;

public sealed class SessionTabState
{
    public string? FilePath { get; set; }
    public string? DraftFile { get; set; }
    public string EncodingLabel { get; set; } = "UTF-8";
    public bool IsRightToLeft { get; set; }

    /// <summary>0 = Normal (content-driven), 1 = RTL, 2 = LTR.
    /// Older session files only have IsRightToLeft.</summary>
    public int DirectionMode { get; set; }
    public double ScrollVertical { get; set; }
    public double ScrollHorizontal { get; set; }
}

public sealed class SessionState
{
    public List<SessionTabState> Tabs { get; set; } = new();
    public int ActiveIndex { get; set; }
}

/// <summary>
/// Persists the working set of tabs (including unsaved drafts) under
/// %AppData%\ArkaSoft.Notepad\session so the session survives restarts and crashes.
/// </summary>
public static class SessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private static string GetSessionDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ArkaSoft.Notepad", "session");

    private static string GetStatePath()
        => Path.Combine(GetSessionDirectory(), "session.json");

    public static SessionState? Load()
    {
        try
        {
            var path = GetStatePath();
            if (!File.Exists(path))
                return null;
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), JsonOptions);
            return state is { Tabs.Count: > 0 } ? state : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SessionState state)
    {
        try
        {
            var directory = GetSessionDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(GetStatePath(), JsonSerializer.Serialize(state, JsonOptions));

            // remove drafts that are no longer referenced by the saved session
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in state.Tabs)
            {
                if (!string.IsNullOrEmpty(tab.DraftFile))
                    keep.Add(GetSafeDraftName(tab.DraftFile));
            }
            foreach (var file in Directory.EnumerateFiles(directory, "draft-*.txt"))
            {
                if (!keep.Contains(Path.GetFileName(file)))
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                }
            }
        }
        catch
        {
            // session persistence is best effort and must never break editing
        }
    }

    public static void Clear()
    {
        try
        {
            var directory = GetSessionDirectory();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    public static string WriteDraft(string content)
    {
        var directory = GetSessionDirectory();
        Directory.CreateDirectory(directory);
        var name = $"draft-{Guid.NewGuid():N}.txt";
        File.WriteAllText(Path.Combine(directory, name), content);
        return name;
    }

    public static string? ReadDraft(string? draftName)
    {
        if (string.IsNullOrEmpty(draftName))
            return null;
        try
        {
            var path = Path.Combine(GetSessionDirectory(), GetSafeDraftName(draftName));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetSafeDraftName(string draftName)
    {
        // never allow the persisted name to escape the session directory
        var safe = Path.GetFileName(draftName.Trim());
        return safe.StartsWith("draft-", StringComparison.OrdinalIgnoreCase) ? safe : "draft-invalid.txt";
    }
}
