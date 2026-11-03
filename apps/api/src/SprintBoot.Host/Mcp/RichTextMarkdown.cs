using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using ModelContextProtocol;

namespace SprintBoot.Host.Mcp;

public record MentionTarget(Guid Id, string Name);

/// <summary>
/// Converts between Markdown, which agents read and write, and the HTML the web editor stores.
/// Output follows the editor schema in apps/web/src/features/issues/richTextExtensions.ts;
/// whatever Markdown cannot express (colors, alignment) stays as inline HTML so nothing is lost.
/// </summary>
public static partial class RichTextMarkdown
{
    private const string MentionScheme = "user:";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough | Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Marked)
        .UseAutoLinks()
        .UseCustomContainers()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private static readonly HtmlParser Parser = new();

    private static readonly HashSet<string> BlockTags =
        ["p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "blockquote", "pre", "hr", "table", "div", "video", "figure"];

    private static readonly HashSet<string> LayoutParents =
        ["body", "ul", "ol", "table", "thead", "tbody", "tfoot", "tr", "blockquote", "div"];

    /// <summary>Mentions are written as @[Name](user:id-or-email); the resolver turns emails into users.</summary>
    public static async Task<string> ToHtmlAsync(string? markdown, Func<string, Task<MentionTarget?>>? resolveUser = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var parsed = Markdown.Parse(markdown.Replace("\r\n", "\n"), Pipeline);
        foreach (var container in parsed.Descendants<CustomContainer>())
        {
            var attributes = container.GetAttributes();
            attributes.Classes?.Clear();
            attributes.AddClass("callout");
            attributes.AddProperty("data-type", "callout");
        }

        foreach (var heading in parsed.Descendants<HeadingBlock>())
        {
            heading.Level = Math.Min(heading.Level, 3);
        }

        var document = Parser.ParseDocument(parsed.ToHtml(Pipeline));
        var body = document.Body!;

        foreach (var list in body.QuerySelectorAll("ul.contains-task-list").ToList())
        {
            ToTaskList(document, list);
        }

        foreach (var item in body.QuerySelectorAll(".task-list-item, .contains-task-list").ToList())
        {
            item.RemoveAttribute("class");
        }

        foreach (var lineBreak in body.QuerySelectorAll("br"))
        {
            if (lineBreak.NextSibling is IText after && after.Data.StartsWith('\n'))
            {
                after.Data = after.Data.TrimStart('\n');
            }
        }

        foreach (var code in body.QuerySelectorAll("pre > code"))
        {
            if (code.TextContent.EndsWith('\n'))
            {
                code.TextContent = code.TextContent[..^1];
            }
        }

        foreach (var link in body.QuerySelectorAll("a").ToList())
        {
            var href = link.GetAttribute("href") ?? string.Empty;
            if (href.StartsWith(MentionScheme, StringComparison.OrdinalIgnoreCase))
            {
                await ToMentionAsync(document, link, href[MentionScheme.Length..], resolveUser);
            }
        }

        RemoveLayoutWhitespace(body);
        return body.InnerHtml;
    }

    public static string ToMarkdown(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var body = Parser.ParseDocument(html).Body!;
        return string.Join("\n\n", Blocks(body.ChildNodes)).Trim();
    }

    private static void ToTaskList(IDocument document, IElement list)
    {
        var items = list.Children.Where(child => child.LocalName == "li").ToList();
        if (items.Count == 0 || !items.All(item => item.ClassList.Contains("task-list-item")))
        {
            return;
        }

        list.RemoveAttribute("class");
        list.SetAttribute("data-type", "taskList");
        foreach (var item in items)
        {
            var box = item.QuerySelector("input[type=checkbox]");
            var done = box?.HasAttribute("checked") == true;
            if (box?.NextSibling is IText after)
            {
                after.Data = after.Data.TrimStart();
            }

            box?.Remove();

            // Loose items wrap their text in a paragraph already; tight ones need one.
            var content = document.CreateElement("div");
            IElement? paragraph = null;
            foreach (var node in item.ChildNodes.ToList())
            {
                if (node is IElement element && BlockTags.Contains(element.LocalName))
                {
                    paragraph = null;
                    content.AppendChild(node);
                    continue;
                }

                if (node is IText { Data: var data } && string.IsNullOrWhiteSpace(data) && paragraph is null)
                {
                    node.RemoveFromParent();
                    continue;
                }

                paragraph ??= (IElement)content.AppendChild(document.CreateElement("p"));
                paragraph.AppendChild(node);
            }

            if (content.ChildNodes.Length == 0)
            {
                content.AppendChild(document.CreateElement("p"));
            }

            item.RemoveAttribute("class");
            item.SetAttribute("data-type", "taskItem");
            item.SetAttribute("data-checked", done ? "true" : "false");
            var label = document.CreateElement("label");
            var input = document.CreateElement("input");
            input.SetAttribute("type", "checkbox");
            if (done)
            {
                input.SetAttribute("checked", "checked");
            }

            label.AppendChild(input);
            label.AppendChild(document.CreateElement("span"));
            item.AppendChild(label);
            item.AppendChild(content);
        }
    }

    private static async Task ToMentionAsync(IDocument document, IElement link, string target, Func<string, Task<MentionTarget?>>? resolveUser)
    {
        var label = link.TextContent.Trim().TrimStart('@');
        MentionTarget? user = Guid.TryParse(target, out var id)
            ? new MentionTarget(id, label)
            : resolveUser is null ? null : await resolveUser(target);
        if (user is null)
        {
            throw new McpException($"Nobody matches the mention {target}. Use find_users to get an email or id.");
        }

        var name = label.Length > 0 && !string.Equals(label, target, StringComparison.OrdinalIgnoreCase) ? label : user.Name;
        var mention = document.CreateElement("span");
        mention.SetAttribute("data-type", "mention");
        mention.SetAttribute("class", "mention");
        mention.SetAttribute("data-id", user.Id.ToString());
        mention.SetAttribute("data-label", name);
        mention.TextContent = "@" + name;

        if (link.PreviousSibling is IText before && before.Data.EndsWith('@'))
        {
            before.Data = before.Data[..^1];
        }

        link.Replace(mention);
    }

    private static void RemoveLayoutWhitespace(IElement root)
    {
        foreach (var text in root.Descendants<IText>().ToList())
        {
            if (string.IsNullOrWhiteSpace(text.Data) && text.ParentElement is { } parent && LayoutParents.Contains(parent.LocalName)
                && (parent.LocalName != "div" || parent.GetAttribute("data-type") == "callout"))
            {
                text.RemoveFromParent();
            }
            else if (text.ParentElement?.LocalName is "li" or "p" or "td" or "th" && text.NextSibling is null or IElement { LocalName: "ul" or "ol" })
            {
                // Markdig ends lines inside tight items and cells with a newline the editor would show as a space.
                text.Data = text.Data.TrimEnd('\n');
            }
        }
    }

    private static List<string> Blocks(INodeList nodes)
    {
        var result = new List<string>();
        var inline = new List<INode>();

        void Flush()
        {
            var text = Inline(inline).Trim();
            if (text.Length > 0)
            {
                result.Add(EscapeLineStarts(text));
            }

            inline.Clear();
        }

        foreach (var node in nodes)
        {
            if (node is IElement element && (BlockTags.Contains(element.LocalName) || element.LocalName == "li"))
            {
                Flush();
                if (Block(element) is { Length: > 0 } block)
                {
                    result.Add(block);
                }
            }
            else
            {
                inline.Add(node);
            }
        }

        Flush();
        return result;
    }

    private static string? Block(IElement element)
    {
        switch (element.LocalName)
        {
            case "p":
                if (HasStyle(element))
                {
                    return element.OuterHtml;
                }

                var text = Inline(element.ChildNodes).Trim();
                return text.Length == 0 ? null : EscapeLineStarts(text);

            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (HasStyle(element))
                {
                    return element.OuterHtml;
                }

                return $"{new string('#', element.LocalName[1] - '0')} {Inline(element.ChildNodes).Trim().Replace("\n", " ")}";

            case "ul" when element.GetAttribute("data-type") == "taskList":
                return TaskList(element);

            case "ul":
                return List(element, ordered: false);

            case "ol":
                return List(element, ordered: true);

            case "li":
                return string.Join("\n\n", Blocks(element.ChildNodes));

            case "blockquote":
                return Prefix(string.Join("\n\n", Blocks(element.ChildNodes)), "> ");

            case "pre":
                var code = element.QuerySelector("code") ?? element;
                var language = code.ClassList.FirstOrDefault(name => name.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..] ?? string.Empty;
                var source = code.TextContent.TrimEnd('\n');
                var fence = new string('`', Math.Max(3, LongestRun(source, '`') + 1));
                return $"{fence}{language}\n{source}\n{fence}";

            case "hr":
                return "---";

            case "table":
                return Table(element);

            case "div" when element.GetAttribute("data-type") == "callout":
                var colons = new string(':', 3 + CalloutDepth(element));
                return $"{colons}callout\n{string.Join("\n\n", Blocks(element.ChildNodes))}\n{colons}";

            default:
                return element.OuterHtml;
        }
    }

    private static string List(IElement list, bool ordered)
    {
        var items = list.Children.Where(child => child.LocalName == "li").ToList();
        var start = int.TryParse(list.GetAttribute("start"), out var first) ? first : 1;
        var blocks = items.Select(item => Blocks(item.ChildNodes)).ToList();
        var loose = blocks.Any(parts => parts.Count(part => !IsListBlock(part)) > 1);

        var lines = blocks.Select((parts, index) =>
        {
            var marker = ordered ? $"{start + index}. " : "- ";
            return Item(marker, parts, marker.Length);
        });
        return string.Join(loose ? "\n\n" : "\n", lines);
    }

    private static string TaskList(IElement list)
    {
        var lines = list.Children.Where(child => child.LocalName == "li").Select(item =>
        {
            var content = item.Children.FirstOrDefault(child => child.LocalName == "div");
            var nodes = content is null
                ? item.ChildNodes.Where(node => node is not IElement { LocalName: "label" }).ToArray()
                : content.ChildNodes.ToArray();
            var parts = BlocksOf(nodes);
            var marker = item.GetAttribute("data-checked") == "true" ? "- [x] " : "- [ ] ";
            return Item(marker, parts, 2);
        });
        return string.Join("\n", lines);
    }

    private static List<string> BlocksOf(INode[] nodes)
    {
        // A detached copy, so the walker can take the nodes without touching the document.
        var holder = Parser.ParseDocument(string.Empty).CreateElement("div");
        foreach (var node in nodes)
        {
            holder.AppendChild(node.Clone());
        }

        return Blocks(holder.ChildNodes);
    }

    private static string Item(string marker, List<string> parts, int indent)
    {
        if (parts.Count == 0)
        {
            return marker.TrimEnd();
        }

        var builder = new StringBuilder(marker).Append(Indent(parts[0], indent, skipFirst: true));
        for (var index = 1; index < parts.Count; index++)
        {
            builder.Append(IsListBlock(parts[index]) && !IsListBlock(parts[index - 1]) ? "\n" : "\n\n");
            builder.Append(Indent(parts[index], indent, skipFirst: false));
        }

        return builder.ToString();
    }

    private static string Table(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").Select(row => row.Children.Where(cell => cell.LocalName is "td" or "th").ToList()).ToList();
        var simple = rows.Count > 0 && rows.All(cells => cells.All(cell =>
            !cell.HasAttribute("colspan") && !cell.HasAttribute("rowspan")
            && cell.Children.All(child => child.LocalName == "p") && cell.Children.Length <= 1
            && cell.QuerySelector("br") is null));
        if (!simple)
        {
            return table.OuterHtml;
        }

        var width = rows.Max(cells => cells.Count);
        string Row(List<IElement> cells) =>
            "| " + string.Join(" | ", Enumerable.Range(0, width).Select(index =>
                index < cells.Count ? Inline(cells[index].ChildNodes.Length == 1 && cells[index].FirstElementChild is { } p ? p.ChildNodes : cells[index].ChildNodes).Trim().Replace("|", "\\|") : string.Empty)) + " |";

        var lines = new List<string> { Row(rows[0]), "| " + string.Join(" | ", Enumerable.Repeat("---", width)) + " |" };
        lines.AddRange(rows.Skip(1).Select(Row));
        return string.Join("\n", lines);
    }

    private static string Inline(IEnumerable<INode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IText text:
                    builder.Append(EscapeInline(Whitespace().Replace(text.Data, " ")));
                    break;

                case IElement element:
                    builder.Append(InlineElement(element));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string InlineElement(IElement element)
    {
        switch (element.LocalName)
        {
            case "strong" or "b":
                return Wrap("**", Inline(element.ChildNodes));

            case "em" or "i":
                return Wrap("*", Inline(element.ChildNodes));

            case "s" or "del" or "strike":
                return Wrap("~~", Inline(element.ChildNodes));

            case "mark" when element.Attributes.Length == 0:
                return Wrap("==", Inline(element.ChildNodes));

            case "code":
                var code = element.TextContent;
                var ticks = new string('`', LongestRun(code, '`') + 1);
                var pad = code.StartsWith('`') || code.EndsWith('`') ? " " : string.Empty;
                return $"{ticks}{pad}{code}{pad}{ticks}";

            case "a":
                var href = element.GetAttribute("href") ?? string.Empty;
                var label = Inline(element.ChildNodes).Trim();
                return label.Length == 0 ? $"<{href}>" : $"[{label}]({Url(href)})";

            case "span" when element.GetAttribute("data-type") == "mention":
                var name = element.GetAttribute("data-label") ?? element.TextContent.TrimStart('@');
                return $"@[{name}]({MentionScheme}{element.GetAttribute("data-id")})";

            case "br":
                return "\n";

            case "img":
                return $"![{element.GetAttribute("alt") ?? string.Empty}]({Url(element.GetAttribute("src") ?? string.Empty)})";

            case "video" or "input":
                return element.OuterHtml;

            default:
                var attributes = string.Concat(element.Attributes.Select(attribute => $" {attribute.Name}=\"{WebUtility.HtmlEncode(attribute.Value)}\""));
                return $"<{element.LocalName}{attributes}>{Inline(element.ChildNodes)}</{element.LocalName}>";
        }
    }

    /// <summary>Markers must touch the text, so edge spaces move outside them.</summary>
    private static string Wrap(string marker, string inner)
    {
        var trimmed = inner.Trim();
        if (trimmed.Length == 0)
        {
            return inner;
        }

        var lead = inner[..(inner.Length - inner.TrimStart().Length)];
        var tail = inner[inner.TrimEnd().Length..];
        return $"{lead}{marker}{trimmed}{marker}{tail}";
    }

    private static string Url(string url) => url.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");

    private static string EscapeInline(string text)
    {
        text = text.Replace("\\", "\\\\").Replace("`", "\\`").Replace("*", "\\*").Replace("](", "\\](");
        text = text.Replace("~~", "\\~\\~").Replace("==", "\\=\\=");
        text = AngleOpen().Replace(text, "\\<");
        text = Entity().Replace(text, "\\&");
        return EdgeUnderscore().Replace(text, "\\_");
    }

    private static string EscapeLineStarts(string text) =>
        string.Join('\n', text.Split('\n').Select(line =>
        {
            var match = BlockStart().Match(line);
            if (match.Success)
            {
                var at = match.Groups["marker"].Index;
                var marker = match.Groups["marker"].Value;
                // "1." escapes the dot; "#", "-", ">" and the rest escape themselves.
                var position = char.IsDigit(marker[0]) ? at + marker.Length - 1 : at;
                return line.Insert(position, "\\");
            }

            return line;
        }));

    private static string Indent(string text, int width, bool skipFirst)
    {
        var pad = new string(' ', width);
        var lines = text.Split('\n');
        return string.Join('\n', lines.Select((line, index) => (index == 0 && skipFirst) || line.Length == 0 ? line : pad + line));
    }

    private static string Prefix(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? prefix.TrimEnd() : prefix + line));

    private static bool IsListBlock(string block) => ListStart().IsMatch(block);

    private static bool HasStyle(IElement element) => element.HasAttribute("style");

    private static int CalloutDepth(IElement element) =>
        element.Children.Select(child => child.LocalName == "div" && child.GetAttribute("data-type") == "callout" ? 1 + CalloutDepth(child) : CalloutDepth(child))
            .DefaultIfEmpty(0).Max();

    private static int LongestRun(string text, char character)
    {
        int longest = 0, current = 0;
        foreach (var item in text)
        {
            current = item == character ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    [GeneratedRegex(@"[ \t\r\n]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<(?=[A-Za-z/!?])")]
    private static partial Regex AngleOpen();

    [GeneratedRegex(@"&(?=#?\w+;)")]
    private static partial Regex Entity();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])_|_(?![\p{L}\p{N}])")]
    private static partial Regex EdgeUnderscore();

    [GeneratedRegex(@"^\s*(?<marker>#{1,6}(?=\s|$)|[>+\-](?=\s|$)|\d+[.)](?=\s|$)|:::|-{3,}\s*$|={3,}\s*$|_{3,}\s*$)")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^(- \[[ x]\] |- |\d+\. )")]
    private static partial Regex ListStart();
}
