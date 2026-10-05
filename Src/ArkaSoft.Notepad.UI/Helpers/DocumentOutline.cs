using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ArkaSoft.Notepad.UI.Helpers;

/// <summary>One entry of the document outline shown in the Explorer sidebar.</summary>
public sealed class OutlineNode : INotifyPropertyChanged
{
    private string _title;
    private bool _isActive;
    private bool _isExpanded = true;

    public OutlineNode(string title, int line)
    {
        _title = title;
        Line = line;
        EndLine = line + 1;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    /// <summary>Only used by the shared TreeViewItem style binding; outline stays expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>Zero-based line the node points at (navigation target).</summary>
    public int Line { get; set; }

    /// <summary>Zero-based line just past the node's span (active-range test).</summary>
    public int EndLine { get; set; }

    public List<OutlineNode> Children { get; } = new();

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Derives a navigable structure from plain text: a heading hierarchy when the
/// document uses Markdown-style "# " headings, otherwise groups of consecutive
/// non-blank lines ("blocks").
/// </summary>
public static partial class DocumentOutline
{
    private const int MaxNodes = 1200;
    private const int MaxBlockChildren = 40;
    private const int MaxTitleLength = 90;

    [GeneratedRegex(@"^(#{1,6})\s+\S")]
    private static partial Regex HeadingRegex();

    public static List<OutlineNode> Build(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1]; // trailing empty element after the final newline

        var headings = CollectHeadings(lines);
        var root = new OutlineNode(string.Empty, 0);

        if (headings.Count > 0)
        {
            var stack = new List<(int Level, OutlineNode Node)>();
            foreach (var (line, level, title) in headings)
            {
                var node = new OutlineNode(title, line);
                while (stack.Count > 0 && stack[^1].Level >= level)
                    stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0)
                    root.Children.Add(node);
                else
                    stack[^1].Node.Children.Add(node);
                stack.Add((level, node));
                if (CountNodes(root) > MaxNodes)
                    break;
            }
        }
        else
        {
            int index = 0;
            int totalNodes = 0;
            while (index < lines.Length && totalNodes < MaxNodes)
            {
                while (index < lines.Length && lines[index].Trim().Length == 0)
                    index++;
                if (index >= lines.Length)
                    break;

                int start = index;
                var node = new OutlineNode(TrimTitle(lines[start]), start);
                index++;

                int added = 0;
                while (index < lines.Length && lines[index].Trim().Length > 0 && added < MaxBlockChildren)
                {
                    node.Children.Add(new OutlineNode(TrimTitle(lines[index]), index));
                    added++;
                    index++;
                    totalNodes++;
                }
                while (index < lines.Length && lines[index].Trim().Length > 0)
                    index++; // skip the rest of an oversized block
                node.EndLine = index;
                root.Children.Add(node);
                totalNodes++;
            }
        }

        SpreadEndLines(root, lines.Length);
        return root.Children;
    }

    private static List<(int Line, int Level, string Title)> CollectHeadings(string[] lines)
    {
        var result = new List<(int, int, string)>();
        var limit = Math.Min(lines.Length, 20000);
        for (int i = 0; i < limit && result.Count <= MaxNodes; i++)
        {
            var match = HeadingRegex().Match(lines[i]);
            if (match.Success)
                result.Add((i, match.Groups[1].Length, lines[i][match.Length..].Trim()));
        }
        return result;
    }

    /// <summary>Extends every node's range to the start of its next sibling so the
    /// active-node test covers the whole document.</summary>
    private static void SpreadEndLines(OutlineNode node, int documentEnd)
    {
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            var next = i + 1 < node.Children.Count
                ? node.Children[i + 1].Line
                : documentEnd;
            child.EndLine = next;
            SpreadEndLines(child, next);
        }
    }

    private static int CountNodes(OutlineNode node)
    {
        int count = 1;
        foreach (var child in node.Children)
            count += CountNodes(child);
        return count;
    }

    private static string TrimTitle(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength] + "…";
    }
}
