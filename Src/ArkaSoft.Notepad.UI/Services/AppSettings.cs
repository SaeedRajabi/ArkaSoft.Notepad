namespace ArkaSoft.Notepad.UI.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

public sealed class PrintSettings
{
    public string PaperName { get; set; } = "A4";
    public bool Landscape { get; set; }
    public double MarginLeftMm { get; set; } = 10;
    public double MarginTopMm { get; set; } = 10;
    public double MarginRightMm { get; set; } = 10;
    public double MarginBottomMm { get; set; } = 10;

    public PrintSettings Clone() => new()
    {
        PaperName = PaperName,
        Landscape = Landscape,
        MarginLeftMm = MarginLeftMm,
        MarginTopMm = MarginTopMm,
        MarginRightMm = MarginRightMm,
        MarginBottomMm = MarginBottomMm
    };
}

public sealed class WindowBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 940;
    public double Height { get; set; } = 640;
    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    public int Theme { get; set; } = (int)AppTheme.System;
    public bool WordWrap { get; set; }
    public double ZoomPercent { get; set; } = 100;
    public bool ShowStatusBar { get; set; } = true;
    public string PersianEditorFont { get; set; } = "Vazir";
    public string EnglishEditorFont { get; set; } = "Cascadia Mono";
    public string DisplayFont { get; set; } = "Segoe UI Variable Display";
    public string InterfaceLanguage { get; set; } = "en";
    public WindowBounds? Window { get; set; }
    public List<string> SessionFiles { get; set; } = new();
    public PrintSettings Print { get; set; } = new();
}
