using System.IO;
using System.Text;

namespace ArkaSoft.Notepad.UI.Services;

public sealed record OpenedFile(string Text, Encoding Encoding, string EncodingLabel);

public sealed class EncodingOption
{
    public string Label { get; }
    public Func<Encoding> Create { get; }

    public EncodingOption(string label, Func<Encoding> create)
    {
        Label = label;
        Create = create;
    }

    public override string ToString() => Label;
}

public static class FileService
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static readonly EncodingOption[] EncodingChoices =
    {
        new("UTF-8", () => Utf8NoBom),
        new("UTF-8 with BOM", () => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
        new("UTF-16 LE", () => new UnicodeEncoding(bigEndian: false, byteOrderMark: true)),
        new("UTF-16 BE", () => new UnicodeEncoding(bigEndian: true, byteOrderMark: true)),
        new("ANSI", () => Encoding.Default)
    };

    public static EncodingOption? FindOption(string label)
        => EncodingChoices.FirstOrDefault(o => string.Equals(o.Label, label, StringComparison.Ordinal));

    public static string LabelFor(Encoding encoding)
    {
        if (encoding is UTF8Encoding utf8)
            return utf8.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8";
        if (Encoding.Unicode.CodePage == encoding.CodePage)
            return "UTF-16 LE";
        if (Encoding.BigEndianUnicode.CodePage == encoding.CodePage)
            return "UTF-16 BE";
        if (Encoding.Default.CodePage == encoding.CodePage)
            return "ANSI";
        return encoding.WebName.ToUpperInvariant();
    }

    /// <summary>Reads a text file, detecting the encoding from the BOM first,
    /// then falling back to strict UTF-8 validation and finally ANSI.</summary>
    public static OpenedFile Open(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            return new OpenedFile(enc.GetString(bytes, 3, bytes.Length - 3), enc, "UTF-8 with BOM");
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var enc = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            return new OpenedFile(enc.GetString(bytes, 2, bytes.Length - 2), enc, "UTF-16 LE");
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var enc = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            return new OpenedFile(enc.GetString(bytes, 2, bytes.Length - 2), enc, "UTF-16 BE");
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return new OpenedFile(strict.GetString(bytes), Utf8NoBom, "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            var ansi = Encoding.Default;
            return new OpenedFile(ansi.GetString(bytes), ansi, "ANSI");
        }
    }

    public static void Save(string path, string text, Encoding encoding)
        => File.WriteAllText(path, text, encoding);

    public static void Rename(string oldPath, string newPath)
    {
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return;
        File.Move(oldPath, newPath, overwrite: false);
    }

    public static string SuggestSaveName(string fileName)
        => string.IsNullOrEmpty(fileName) ? "Untitled.txt" : fileName;
}
