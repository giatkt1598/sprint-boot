using AngleSharp.Html.Parser;
using Ganss.Xss;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.Issues;

/// <summary>All user HTML passes through here before it is stored.</summary>
public class RichTextSanitizer : ISingletonDependency
{
    private readonly HtmlSanitizer _sanitizer = new();

    public RichTextSanitizer()
    {
        _sanitizer.AllowDataAttributes = true;
        _sanitizer.AllowedAttributes.Add("class");
    }

    /// <summary>User ids of the mentions in already sanitized HTML.</summary>
    public static Guid[] Mentions(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        return [.. document.QuerySelectorAll("[data-type='mention']")
            .Select(node => Guid.TryParse(node.GetAttribute("data-id"), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()];
    }

    public (string Html, string Text) Clean(string? html)
    {
        var clean = _sanitizer.Sanitize(html ?? string.Empty);
        var text = new HtmlParser().ParseDocument(clean).Body?.TextContent ?? string.Empty;
        return (clean, text.Trim());
    }
}
