using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.Issues;

/// <summary>All user HTML passes through here before it is stored.</summary>
public partial class RichTextSanitizer : ISingletonDependency
{
    private readonly HtmlSanitizer _sanitizer = new();

    public RichTextSanitizer()
    {
        _sanitizer.AllowDataAttributes = true;
        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowedAttributes.Add("rel");
        _sanitizer.AllowedAttributes.Add("target");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.AllowedTags.Add("video");
        _sanitizer.AllowedAttributes.Add("controls");

        // Only what the editor writes, so stored text cannot reposition or restyle the page around it.
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedCssProperties.UnionWith(["color", "background-color", "text-align"]);
        _sanitizer.PostProcessDom += (_, args) => Restrict(args.Document);
    }

    private static void Restrict(AngleSharp.Html.Dom.IHtmlDocument document)
    {
        foreach (var input in document.QuerySelectorAll("input").ToList())
        {
            if (input.GetAttribute("type") == "checkbox")
            {
                // Saved text is read-only; a checkbox changes through editing.
                input.SetAttribute("disabled", "disabled");
            }
            else
            {
                input.Remove();
            }
        }

        foreach (var element in document.QuerySelectorAll("[class]").ToList())
        {
            var kept = (element.GetAttribute("class") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(name => AllowedClass().IsMatch(name));
            var value = string.Join(' ', kept);
            if (value.Length == 0)
            {
                element.RemoveAttribute("class");
            }
            else
            {
                element.SetAttribute("class", value);
            }
        }
    }

    [GeneratedRegex("^(mention|callout|language-[a-z0-9+#-]+)$")]
    private static partial Regex AllowedClass();

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
        var clean = RichTextFileUrls.StripSignatures(_sanitizer.Sanitize(html ?? string.Empty));
        var text = new HtmlParser().ParseDocument(clean).Body?.TextContent ?? string.Empty;
        return (clean, text.Trim());
    }
}
