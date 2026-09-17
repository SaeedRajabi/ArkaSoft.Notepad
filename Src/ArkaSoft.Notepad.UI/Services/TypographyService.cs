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
        => new(new Uri("pack://application:,,,/"), $"{FontSource(name, "Vazir")}, {VazirSource}, Segoe UI, Segoe UI Emoji");

    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current.Resources;
        resources["F.Ui"] = CreateDisplayFont(settings.DisplayFont);
        resources["F.Editor"] = CreateEditorFont(settings.PersianEditorFont, settings.EnglishEditorFont);
    }
}
