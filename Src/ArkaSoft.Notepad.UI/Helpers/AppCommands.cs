using System.Windows.Input;

namespace ArkaSoft.Notepad.UI.Helpers;

public static class AppCommands
{
    public static readonly RoutedUICommand NewTab = new(
        "New tab", "NewTab", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.T, ModifierKeys.Control) });

    public static readonly RoutedUICommand NewWindow = new(
        "New window", "NewWindow", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Shift) });

    public static readonly RoutedUICommand Open = new(
        "Open", "Open", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.O, ModifierKeys.Control) });

    public static readonly RoutedUICommand Save = new(
        "Save", "Save", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.S, ModifierKeys.Control) });

    public static readonly RoutedUICommand SaveAs = new(
        "Save as", "SaveAs", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift) });

    public static readonly RoutedUICommand Rename = new(
        "Rename", "Rename", typeof(AppCommands));

    public static readonly RoutedUICommand PageSetup = new(
        "Page setup", "PageSetup", typeof(AppCommands));

    public static readonly RoutedUICommand Print = new(
        "Print", "Print", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.P, ModifierKeys.Control) });

    public static readonly RoutedUICommand Exit = new(
        "Exit", "Exit", typeof(AppCommands));

    public static readonly RoutedUICommand Find = new(
        "Find", "Find", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.F, ModifierKeys.Control) });

    public static readonly RoutedUICommand FindNext = new(
        "Find next", "FindNext", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.F3, ModifierKeys.None) });

    public static readonly RoutedUICommand FindPrevious = new(
        "Find previous", "FindPrevious", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.F3, ModifierKeys.Shift) });

    public static readonly RoutedUICommand Replace = new(
        "Replace", "Replace", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.H, ModifierKeys.Control) });

    public static readonly RoutedUICommand GoTo = new(
        "Go to", "GoTo", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.G, ModifierKeys.Control) });

    public static readonly RoutedUICommand InsertTimeDate = new(
        "Time/date", "InsertTimeDate", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.F5, ModifierKeys.None) });

    public static readonly RoutedUICommand ZoomIn = new(
        "Zoom in", "ZoomIn", typeof(AppCommands));

    public static readonly RoutedUICommand ZoomOut = new(
        "Zoom out", "ZoomOut", typeof(AppCommands));

    public static readonly RoutedUICommand ZoomReset = new(
        "Restore default zoom", "ZoomReset", typeof(AppCommands));

    public static readonly RoutedUICommand ToggleWordWrap = new(
        "Word wrap", "ToggleWordWrap", typeof(AppCommands));

    public static readonly RoutedUICommand ToggleStatusBar = new(
        "Status bar", "ToggleStatusBar", typeof(AppCommands));

    public static readonly RoutedUICommand FontSettings = new(
        "Font settings", "FontSettings", typeof(AppCommands));

    public static readonly RoutedUICommand SetThemeSystem = new("Use system setting", "SetThemeSystem", typeof(AppCommands));
    public static readonly RoutedUICommand SetThemeLight = new("Light", "SetThemeLight", typeof(AppCommands));
    public static readonly RoutedUICommand SetThemeDark = new("Dark", "SetThemeDark", typeof(AppCommands));

    public static readonly RoutedUICommand NextTab = new("Next tab", "NextTab", typeof(AppCommands));
    public static readonly RoutedUICommand PreviousTab = new("Previous tab", "PreviousTab", typeof(AppCommands));

    public static readonly RoutedUICommand ToggleReadingDirection = new(
        "Right-to-left reading direction", "ToggleReadingDirection", typeof(AppCommands));

    public static readonly RoutedUICommand InsertEmoji = new(
        "Emoji", "InsertEmoji", typeof(AppCommands));

    public static readonly RoutedUICommand ToggleLineNumbers = new(
        "Line numbers", "ToggleLineNumbers", typeof(AppCommands));

    public static readonly RoutedUICommand ToggleSidebar = new(
        "Explorer", "ToggleSidebar", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.B, ModifierKeys.Control) });

    public static readonly RoutedUICommand FileAssociations = new(
        "File associations", "FileAssociations", typeof(AppCommands));

    public static readonly RoutedUICommand About = new(
        "About", "About", typeof(AppCommands));

    public static readonly RoutedUICommand ToggleSessionRestore = new(
        "Restore previous session", "ToggleSessionRestore", typeof(AppCommands));

    public static readonly RoutedUICommand UpperCase = new(
        "UPPERCASE", "UpperCase", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.U, ModifierKeys.Control | ModifierKeys.Shift) });

    public static readonly RoutedUICommand LowerCase = new(
        "lowercase", "LowerCase", typeof(AppCommands),
        new InputGestureCollection { new KeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Shift) });

    public static readonly RoutedUICommand TitleCase = new(
        "Title Case", "TitleCase", typeof(AppCommands));

    public static readonly RoutedUICommand TrimTrailing = new(
        "Trim trailing whitespace", "TrimTrailing", typeof(AppCommands));

    public static readonly RoutedUICommand SortLinesAsc = new(
        "Sort lines A to Z", "SortLinesAsc", typeof(AppCommands));

    public static readonly RoutedUICommand SortLinesDesc = new(
        "Sort lines Z to A", "SortLinesDesc", typeof(AppCommands));

    public static readonly RoutedUICommand RemoveDuplicateLines = new(
        "Remove duplicate lines", "RemoveDuplicateLines", typeof(AppCommands));
}
