using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace SprintBoot.Host.Mcp;

public class DescriptionEdit
{
    [Description("replace, append, prepend, replace_section or insert_after_section.")]
    public required string Op { get; set; }

    [Description("replace: the exact Markdown to find, as get_issue returns it.")]
    public string? Find { get; set; }

    [Description("The new Markdown: the replacement, the text to add, or the new section body.")]
    public string? Text { get; set; }

    [Description("replace_section and insert_after_section: the heading text, without the # marks.")]
    public string? Heading { get; set; }

    [Description("replace: change every match instead of requiring exactly one.")]
    public bool All { get; set; }
}

/// <summary>Partial edits on a Markdown document, so an agent can change one part without resending the rest.</summary>
public static partial class MarkdownEdits
{
    public static string Apply(string markdown, IEnumerable<DescriptionEdit> edits)
    {
        var text = markdown.Replace("\r\n", "\n");
        var step = 0;
        foreach (var edit in edits)
        {
            step++;
            text = edit.Op.Trim().ToLowerInvariant() switch
            {
                "replace" => Replace(text, edit, step),
                "append" => Join(text, Required(edit.Text, step, "text")),
                "prepend" => Join(Required(edit.Text, step, "text"), text),
                "replace_section" => ReplaceSection(text, edit, step),
                "insert_after_section" => InsertAfterSection(text, edit, step),
                _ => throw new McpException($"Edit {step}: unknown op {edit.Op}. Use replace, append, prepend, replace_section or insert_after_section."),
            };
        }

        return text.Trim();
    }

    private static string Replace(string text, DescriptionEdit edit, int step)
    {
        var find = Required(edit.Find, step, "find");
        var count = Count(text, find);
        if (count == 0)
        {
            throw new McpException($"Edit {step}: the text to find is not in the description. Read the issue again and copy it exactly.");
        }

        if (count > 1 && !edit.All)
        {
            throw new McpException($"Edit {step}: the text to find appears {count} times. Add surrounding text to make it unique, or set all.");
        }

        var replacement = edit.Text ?? string.Empty;
        if (edit.All)
        {
            return text.Replace(find, replacement, StringComparison.Ordinal);
        }

        var at = text.IndexOf(find, StringComparison.Ordinal);
        return string.Concat(text.AsSpan(0, at), replacement, text.AsSpan(at + find.Length));
    }

    private static string ReplaceSection(string text, DescriptionEdit edit, int step)
    {
        var lines = text.Split('\n').ToList();
        var (start, end) = FindSection(lines, Required(edit.Heading, step, "heading"), step);
        var body = string.IsNullOrWhiteSpace(edit.Text) ? [] : new[] { string.Empty }.Concat(edit.Text.Trim().Split('\n')).Append(string.Empty);
        lines.RemoveRange(start + 1, end - start - 1);
        lines.InsertRange(start + 1, body);
        return string.Join('\n', lines);
    }

    private static string InsertAfterSection(string text, DescriptionEdit edit, int step)
    {
        var lines = text.Split('\n').ToList();
        var (_, end) = FindSection(lines, Required(edit.Heading, step, "heading"), step);
        var before = string.Join('\n', lines.Take(end));
        var after = string.Join('\n', lines.Skip(end));
        return Join(Join(before, Required(edit.Text, step, "text")), after);
    }

    /// <summary>The heading line and the line after the section: the next heading of the same or a higher level.</summary>
    private static (int Start, int End) FindSection(List<string> lines, string heading, int step)
    {
        var wanted = Normalize(heading.TrimStart('#'));
        var headings = Headings(lines).ToList();
        var matches = headings.Where(item => Normalize(item.Text) == wanted).ToList();
        if (matches.Count == 0)
        {
            var known = string.Join(", ", headings.Select(item => item.Text));
            throw new McpException($"Edit {step}: no heading named {heading}. Headings: {(known.Length == 0 ? "none" : known)}.");
        }

        if (matches.Count > 1)
        {
            throw new McpException($"Edit {step}: {matches.Count} headings are named {heading}; edit with replace instead.");
        }

        var match = matches[0];
        var next = headings.FirstOrDefault(item => item.Line > match.Line && item.Level <= match.Level);
        return (match.Line, next.Text is null ? lines.Count : next.Line);
    }

    private static IEnumerable<(int Line, int Level, string Text)> Headings(List<string> lines)
    {
        string? fence = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var fenceMatch = Fence().Match(line);
            if (fenceMatch.Success)
            {
                var marker = fenceMatch.Groups[1].Value;
                if (fence is null)
                {
                    fence = marker;
                }
                else if (marker.StartsWith(fence, StringComparison.Ordinal))
                {
                    fence = null;
                }

                continue;
            }

            if (fence is null && Heading().Match(line) is { Success: true } heading)
            {
                yield return (index, heading.Groups[1].Length, heading.Groups[2].Value.Trim());
            }
        }
    }

    private static string Normalize(string heading) => Regex.Replace(heading.Trim(), @"\s+", " ").ToLowerInvariant();

    private static string Join(string first, string second) =>
        first.Trim().Length == 0 ? second.Trim()
        : second.Trim().Length == 0 ? first.TrimEnd()
        : $"{first.TrimEnd()}\n\n{second.Trim()}";

    private static int Count(string text, string find)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(find, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += find.Length;
        }

        return count;
    }

    private static string Required(string? value, int step, string name) =>
        string.IsNullOrEmpty(value) ? throw new McpException($"Edit {step}: {name} is required.") : value;

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();
}
