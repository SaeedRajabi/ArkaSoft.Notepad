using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace ArkaSoft.Notepad.UI.Services;

public static class LocalizationService
{
    private static readonly (string Key, string English, string Persian)[] Strings =
    [
        ("AppName", "Notepad", "یادداشت"),
        ("Untitled", "Untitled", "بدون عنوان"),
        ("File", "_File", "_فایل"), ("Edit", "_Edit", "_ویرایش"), ("View", "_View", "_نمایش"),
        ("NewTab", "New tab", "زبانهٔ جدید"), ("NewWindow", "New window", "پنجرهٔ جدید"),
        ("Open", "Open...", "باز کردن…"), ("Save", "Save", "ذخیره"), ("SaveAs", "Save as...", "ذخیره با نام…"),
        ("Rename", "Rename", "تغییر نام"), ("PageSetup", "Page setup", "تنظیم صفحه"),
        ("Print", "Print", "چاپ"), ("Exit", "Exit", "خروج"), ("Close", "Close", "بستن"),
        ("Undo", "Undo", "واگرد"), ("Redo", "Redo", "بازانجام"), ("Cut", "Cut", "برش"),
        ("Copy", "Copy", "کپی"), ("Paste", "Paste", "چسباندن"), ("Delete", "Delete", "حذف"),
        ("Find", "Find", "جست‌وجو"), ("FindNext", "Find next", "نتیجهٔ بعدی"),
        ("FindPrevious", "Find previous", "نتیجهٔ قبلی"), ("Replace", "Replace", "جایگزینی"),
        ("ReplaceAll", "Replace all", "جایگزینی همه"), ("GoTo", "Go to", "رفتن به خط"),
        ("SelectAll", "Select all", "انتخاب همه"), ("TimeDate", "Time/date", "تاریخ و ساعت"),
        ("Zoom", "Zoom", "بزرگ‌نمایی"), ("ZoomIn", "Zoom in", "بزرگ‌تر"), ("ZoomOut", "Zoom out", "کوچک‌تر"),
        ("ZoomReset", "Restore default zoom", "بزرگ‌نمایی پیش‌فرض"),
        ("WordWrap", "Word wrap", "شکستن خطوط بلند"), ("StatusBar", "Status bar", "نوار وضعیت"),
        ("Theme", "App theme", "پوستهٔ برنامه"), ("System", "Use system setting", "پیروی از سیستم"),
        ("Light", "Light", "روشن"), ("Dark", "Dark", "تاریک"),
        ("Fonts", "Fonts and language...", "فونت و زبان…"), ("FontSettings", "Fonts and language", "تنظیم فونت و زبان"),
        ("InterfaceLanguage", "Interface language", "زبان رابط برنامه"),
        ("PersianFont", "Persian writing font", "فونت نوشتاری فارسی"),
        ("EnglishFont", "English writing font", "فونت نوشتاری انگلیسی"),
        ("DisplayFont", "Interface font", "فونت نمایشی برنامه"),
        ("FontHelp", "Vazir is included. Your font choices apply to existing and new text.", "فونت وزیر همراه برنامه است. انتخاب فونت روی متن‌های قبلی و جدید اعمال می‌شود."),
        ("DirectionHelp", "Keyboard language sets the direction of an empty paragraph. Switching languages inside a sentence keeps its direction.", "زبان کیبورد، جهت پاراگراف خالی را تعیین می‌کند. تغییر زبان در میان جمله، جهت آن را حفظ می‌کند."),
        ("DisplayPreview", "Interface preview — پیش‌نمایش رابط", "پیش‌نمایش رابط — Interface preview"),
        ("Cancel", "Cancel", "انصراف"), ("OK", "OK", "تأیید"), ("DontSave", "Don't save", "ذخیره نشود"),
        ("Yes", "Yes", "بله"), ("No", "No", "خیر"),
        ("RTL", "Right-to-left paragraph", "پاراگراف راست‌به‌چپ"), ("Emoji", "Emoji", "شکلک"),
        ("Minimize", "Minimize", "کمینه"), ("Maximize", "Maximize", "بیشینه"),
        ("NewTabTip", "New tab (Ctrl+T)", "زبانهٔ جدید (Ctrl+T)"),
        ("CloseTabTip", "Close tab (Ctrl+W)", "بستن زبانه (Ctrl+W)"),
        ("MatchCase", "Match case", "تطبیق حروف بزرگ و کوچک"),
        ("PreviousMatch", "Previous match (Shift+Enter)", "نتیجهٔ قبلی (Shift+Enter)"),
        ("NextMatch", "Next match (Enter)", "نتیجهٔ بعدی (Enter)"),
        ("ToggleReplace", "Toggle replace", "نمایش جایگزینی"), ("CloseFind", "Close (Esc)", "بستن (Esc)"),
        ("NoResults", "0 results", "بدون نتیجه"), ("Replaced", "{0} replaced", "{0} مورد جایگزین شد"),
        ("Line", "Ln {0}", "خط {0}"), ("Column", ", Col {0}", "، ستون {0}"),
        ("Lines", "{0} lines", "{0} خط"), ("OneLine", "1 line", "۱ خط"),
        ("Characters", "{0} characters", "{0} نویسه"), ("OneCharacter", "1 character", "۱ نویسه"),
        ("Encoding", "Choose encoding", "انتخاب کدگذاری"),
        ("EncodingHelp", "The file will be saved with the selected encoding.", "فایل با کدگذاری انتخاب‌شده ذخیره می‌شود."),
        ("PaperSize", "Paper size", "اندازهٔ کاغذ"), ("Orientation", "Orientation", "جهت کاغذ"),
        ("Portrait", "Portrait", "عمودی"), ("Landscape", "Landscape", "افقی"),
        ("Margins", "Margins (mm)", "حاشیه‌ها (میلی‌متر)"),
        ("Left", "Left", "چپ"), ("Right", "Right", "راست"), ("Top", "Top", "بالا"), ("Bottom", "Bottom", "پایین"),
        ("InvalidMargins", "Margins must be numbers between 0 and 50 millimeters.", "حاشیه‌ها باید عددی بین صفر و ۵۰ میلی‌متر باشند."),
        ("FileFilter", "Text documents (*.txt)|*.txt|All files (*.*)|*.*", "فایل‌های متنی (*.txt)|*.txt|همهٔ فایل‌ها (*.*)|*.*"),
        ("OpenError", "Could not open '{0}':\n{1}", "باز کردن «{0}» ممکن نشد:\n{1}"),
        ("SaveError", "Could not save '{0}':\n{1}", "ذخیرهٔ «{0}» ممکن نشد:\n{1}"),
        ("RenameError", "Could not rename the file:\n{0}", "تغییر نام فایل ممکن نشد:\n{0}"),
        ("SaveChanges", "Do you want to save changes to:\n{0}", "تغییرات این فایل ذخیره شود؟\n{0}"),
        ("SaveBeforeRename", "Save the file before renaming it.", "ابتدا فایل را ذخیره کنید، سپس نام آن را تغییر دهید."),
        ("FileName", "File name:", "نام فایل:"), ("EnterFileName", "Enter a file name.", "نام فایل را وارد کنید."),
        ("InvalidFileName", "A file name cannot contain any of these characters: {0}", "نام فایل نباید شامل این نویسه‌ها باشد: {0}"),
        ("GoToUnavailable", "Go To is unavailable when word wrap is enabled.", "برای رفتن به شمارهٔ خط، شکستن خطوط بلند را غیرفعال کنید."),
        ("LineNumber", "Line number (1 - {0}):", "شمارهٔ خط (۱ تا {0}):"),
        ("InvalidLine", "Enter a line number between 1 and {0}.", "شمارهٔ خطی بین ۱ و {0} وارد کنید."),
        ("PrintError", "Printing failed:\n{0}", "چاپ انجام نشد:\n{0}"),
        ("UnexpectedError", "An unexpected error occurred:\n{0}\n\nDetails were written to error.log.", "خطای غیرمنتظره رخ داد:\n{0}\n\nجزئیات در error.log ثبت شد.")
    ];

    public static event Action? Changed;
    public static string Language { get; private set; } = "en";

    public static void Apply(string? language)
    {
        Language = language == "fa" ? "fa" : "en";
        var resources = Application.Current.Resources;
        foreach (var (key, english, persian) in Strings)
            resources["T." + key] = Language == "fa" ? persian : english;
        resources["Ui.Direction"] = Language == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        resources["Ui.Language"] = XmlLanguage.GetLanguage(Language == "fa" ? "fa-IR" : "en-US");
        Changed?.Invoke();
    }

    public static string Get(string key, params object[] args)
    {
        var entry = Strings.FirstOrDefault(item => item.Key == key);
        var text = Language == "fa" ? entry.Persian : entry.English;
        text ??= key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}
