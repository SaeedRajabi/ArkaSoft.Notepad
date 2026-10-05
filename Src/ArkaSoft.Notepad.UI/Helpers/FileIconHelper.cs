using System.IO;
using System.Windows.Media;

namespace ArkaSoft.Notepad.UI.Helpers;

/// <summary>
/// VS Code-style colored badges for file types: a short label and a color per
/// extension. Unknown extensions fall back to the plain page glyph.
/// </summary>
public static class FileIconHelper
{
    public static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>(label, background hex, foreground hex) for a file name;
    /// label empty means "use the generic page glyph". Every known extension
    /// gets its own color; unknown ones get a stable color derived from the
    /// extension text itself, so distinct types stay visually distinct.</summary>
    public static (string Label, string Background, string Foreground) For(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var lower = name.ToLowerInvariant();

        // special file names without a usable extension
        if (lower is ".gitignore" or ".gitattributes" or ".gitmodules")
            return ("GIT", "#F14E32", "#FFFFFF");
        if (lower is "makefile" or "dockerfile" or "cmakelists.txt")
            return ("MK", "#5C7CFA", "#FFFFFF");

        var ext = Path.GetExtension(lower);
        switch (ext)
        {
            case ".cs": return ("C#", "#2EA043", "#FFFFFF");
            case ".csproj": return ("CSP", "#2EA043", "#FFFFFF");
            case ".sln": return ("SLN", "#68217A", "#FFFFFF");
            case ".py": return ("PY", "#3572A5", "#FFFFFF");
            case ".html" or ".htm": return ("<>", "#E44D26", "#FFFFFF");
            case ".css": return ("CSS", "#663399", "#FFFFFF");
            case ".js": return ("JS", "#E8D44D", "#323330");
            case ".ts": return ("TS", "#3178C6", "#FFFFFF");
            case ".json": return ("{ }", "#CBCB41", "#323330");
            case ".md": return ("MD", "#519ABA", "#FFFFFF");
            case ".xml": return ("XML", "#0060AC", "#FFFFFF");
            case ".xaml": return ("X", "#0F6BB8", "#FFFFFF");
            case ".txt": return ("TXT", "#6B8086", "#FFFFFF");
            case ".log": return ("LOG", "#94A187", "#FFFFFF");
            case ".pdf": return ("PDF", "#D93831", "#FFFFFF");
            case ".c" or ".h": return ("C", "#555E70", "#FFFFFF");
            case ".cpp" or ".hpp": return ("C++", "#F34B7D", "#FFFFFF");
            case ".java": return ("J", "#B07219", "#FFFFFF");
            case ".sql": return ("SQL", "#DD7B33", "#FFFFFF");
            case ".zip" or ".rar" or ".7z": return ("ZIP", "#E8A33D", "#FFFFFF");
            case ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".svg":
                return ("IMG", "#A074C4", "#FFFFFF");
            case ".mp3" or ".wav" or ".flac" or ".ogg": return ("AUD", "#DB7093", "#FFFFFF");
            case ".mp4" or ".avi" or ".mkv" or ".mov": return ("VID", "#4682B4", "#FFFFFF");
            case ".exe" or ".dll": return ("EXE", "#9AA0A6", "#FFFFFF");
            case ".bat" or ".cmd" or ".ps1" or ".sh": return ("SH", "#89E051", "#1B1B1B");
            case ".yml" or ".yaml": return ("YML", "#CB171E", "#FFFFFF");
            case ".ini" or ".cfg" or ".config": return ("CFG", "#7A8B99", "#FFFFFF");
            case ".doc" or ".docx": return ("DOC", "#2B579A", "#FFFFFF");
            case ".xls" or ".xlsx" or ".csv": return ("XLS", "#217346", "#FFFFFF");
        }

        if (ext.Length > 1)
        {
            var label = ext[1..].ToUpperInvariant();
            if (label.Length > 3)
                label = label[..3];
            int hash = 0;
            foreach (var ch in label)
                hash = hash * 31 + ch;
            return (label, Palette[Math.Abs(hash) % Palette.Length], "#FFFFFF");
        }
        return (string.Empty, string.Empty, string.Empty);
    }

    private static readonly string[] Palette =
    [
        "#6B8086", "#8A5CF6", "#E8590C", "#0CA678", "#3B82F6", "#D6336C",
        "#F59F00", "#5C7CFA", "#20C997", "#F06595", "#748FFC", "#FFA94D",
        "#38D9A9", "#91A7FF", "#FF8787", "#63E6BE", "#B197FC", "#FFC078"
    ];
}
