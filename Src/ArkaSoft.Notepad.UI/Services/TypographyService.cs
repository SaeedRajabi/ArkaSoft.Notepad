using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ArkaSoft.Notepad.UI.Services;

public static class TypographyService
{
    // Anonymous composite fonts do not carry a XAML base URI. Ship the font next
    // to the app too, so their maps resolve an absolute file URI in build/publish.
    public static string VazirSource => new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar).AbsoluteUri + "#Vazir";
    public const string PersianRange = "0600-06FF,0750-077F,0870-089F,08A0-08FF,FB50-FDFF,FE70-FEFF";
    private const string PersianDigitsRange = "0660-0669,06F0-06F9";
    private const string LatinDigitsRange = "0030-0039";

    public static string FontSource(string? name, string fallback)
    {
        name = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        return name.Equals("Vazir", StringComparison.OrdinalIgnoreCase) ? VazirSource : name;
    }

    // A fallback list alone would let an English font with Arabic glyphs override
    // the user's Persian choice. Map scripts explicitly without splitting text runs.
    public static FontFamily CreateEditorFont(string? persian, string? english)
    {
        var family = new FontFamily();
        // Some user-selected display fonts omit Arabic-Indic or Latin digits.
        // Give numbers a glyph-complete fallback before applying script fonts.
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = PersianDigitsRange,
            Target = "Tahoma, Segoe UI"
        });
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = LatinDigitsRange,
            Target = "Segoe UI, Tahoma"
        });
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = PersianRange,
            Target = $"{FontSource(persian, "Vazir")}, {VazirSource}, Tahoma"
        });
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = "0000-10FFFF",
            Target = $"{FontSource(english, "Consolas")}, Consolas, Segoe UI, Segoe UI Emoji"
        });
        return family;
    }

    public static FontFamily CreateDisplayFont(string? name)
    {
        var family = new FontFamily();
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = PersianDigitsRange, Target = "Tahoma, Segoe UI" });
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = LatinDigitsRange, Target = "Segoe UI, Tahoma" });
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = "0000-10FFFF",
            Target = $"{FontSource(name, "Vazir")}, {VazirSource}, Segoe UI, Segoe UI Emoji"
        });
        return family;
    }

    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current.Resources;
        resources["F.Ui"] = CreateDisplayFont(settings.DisplayFont);
        resources["F.Editor"] = CreateEditorFont(settings.PersianEditorFont, settings.EnglishEditorFont);

        // Per-paragraph fonts: RTL paragraphs render entirely in the Persian
        // font (spaces and punctuation included), LTR paragraphs entirely in
        // the English one — no more mixed-font word spacing inside a line.
        PersianEditorFontFamily = new FontFamily($"{FontSource(settings.PersianEditorFont, "Vazir")}, Tahoma");
        EnglishEditorFontFamily = CreateEditorFont(settings.PersianEditorFont, settings.EnglishEditorFont);
    }

    /// <summary>Font for right-to-left (Persian) paragraphs.</summary>
    public static FontFamily PersianEditorFontFamily { get; private set; } = new FontFamily("Vazir");

    /// <summary>Font for left-to-right (English) paragraphs.</summary>
    public static FontFamily EnglishEditorFontFamily { get; private set; } = new FontFamily("Consolas");
}
