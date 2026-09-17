using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Dialogs;

public partial class FontSettingsDialog : Window
{
    private sealed record LanguageChoice(string Code, string Label);

    public FontSettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        var names = Fonts.SystemFontFamilies.Select(font => font.Source)
            .Concat(new[] { "Vazir", settings.PersianEditorFont, settings.EnglishEditorFont, settings.DisplayFont })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var box in new[] { PersianFontBox, EnglishFontBox, DisplayFontBox })
        {
            box.ItemsSource = names;
            box.SelectionChanged += Font_Changed;
        }
        PersianFontBox.SelectedItem = string.IsNullOrWhiteSpace(settings.PersianEditorFont) ? "Vazir" : settings.PersianEditorFont;
        EnglishFontBox.SelectedItem = string.IsNullOrWhiteSpace(settings.EnglishEditorFont) ? "Consolas" : settings.EnglishEditorFont;
        DisplayFontBox.SelectedItem = string.IsNullOrWhiteSpace(settings.DisplayFont) ? "Vazir" : settings.DisplayFont;
        LanguageBox.ItemsSource = new[] { new LanguageChoice("en", "English"), new LanguageChoice("fa", "فارسی") };
        LanguageBox.SelectedValue = settings.InterfaceLanguage == "fa" ? "fa" : "en";
        UpdatePreview();
    }

    public static bool Show(Window owner, AppSettings settings)
    {
        var dialog = new FontSettingsDialog(settings) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return false;
        settings.PersianEditorFont = SelectedFont(dialog.PersianFontBox, "Vazir");
        settings.EnglishEditorFont = SelectedFont(dialog.EnglishFontBox, "Consolas");
        settings.DisplayFont = SelectedFont(dialog.DisplayFontBox, "Vazir");
        settings.InterfaceLanguage = dialog.LanguageBox.SelectedValue as string ?? "en";
        return true;
    }

    private static string SelectedFont(ComboBox box, string fallback) => box.SelectedItem as string ?? fallback;
    private void Font_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PersianPreview is null || EnglishFontBox is null || DisplayPreview is null)
            return;
        var font = TypographyService.CreateEditorFont(SelectedFont(PersianFontBox, "Vazir"), SelectedFont(EnglishFontBox, "Consolas"));
        PersianPreview.FontFamily = EnglishPreview.FontFamily = font;
        DisplayPreview.FontFamily = TypographyService.CreateDisplayFont(SelectedFont(DisplayFontBox, "Vazir"));
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Title_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
