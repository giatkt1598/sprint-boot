using Shouldly;
using SprintBoot.Issues;
using Xunit;

namespace SprintBoot.UnitTests;

public class RichTextSanitizerTests
{
    private static string Clean(string html) => new RichTextSanitizer().Clean(html).Html;

    [Theory]
    [InlineData("<h2>Title</h2>", "<h2>")]
    [InlineData("<ul data-type=\"taskList\"><li data-checked=\"true\" data-type=\"taskItem\"><label><input type=\"checkbox\" checked=\"checked\"><span></span></label><div><p>x</p></div></li></ul>", "data-checked=\"true\"")]
    [InlineData("<table><tbody><tr><th colspan=\"2\" rowspan=\"1\"><p>a</p></th></tr></tbody></table>", "colspan=\"2\"")]
    [InlineData("<blockquote><p>a</p></blockquote>", "<blockquote>")]
    [InlineData("<div data-type=\"callout\" class=\"callout\"><p>a</p></div>", "class=\"callout\"")]
    [InlineData("<pre><code class=\"language-csharp\">a</code></pre>", "language-csharp")]
    [InlineData("<p><mark data-color=\"#FEE2E2\" style=\"background-color: #FEE2E2; color: inherit\">a</mark></p>", "background-color")]
    [InlineData("<p style=\"text-align: center\">a</p>", "text-align")]
    [InlineData("<p><span style=\"color: #B91C1C\">a</span></p>", "color")]
    [InlineData("<p><s>a</s><u>b</u></p>", "<s>")]
    [InlineData("<hr>", "<hr")]
    [InlineData("<img src=\"https://x.test/a.png\">", "src=\"https://x.test/a.png\"")]
    [InlineData("<img src=\"https://x.test/a.png\" width=\"320\" height=\"180\">", "width=\"320\" height=\"180\"")]
    [InlineData("<a href=\"mailto:a@b.c\" rel=\"noopener noreferrer\" target=\"_blank\">a</a>", "mailto:a@b.c")]
    [InlineData("<span data-type=\"mention\" data-id=\"1\" class=\"mention\">@a</span>", "class=\"mention\"")]
    public void Everything_the_editor_writes_survives(string html, string expected)
    {
        Clean(html).ShouldContain(expected);
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">a</a>", "javascript")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">", "onerror")]
    [InlineData("<script>alert(1)</script>", "script")]
    [InlineData("<p style=\"position: fixed; top: 0\">a</p>", "position")]
    [InlineData("<div class=\"fixed inset-0\">a</div>", "inset-0")]
    [InlineData("<input type=\"text\" value=\"a\">", "input")]
    public void Dangerous_markup_is_removed(string html, string forbidden)
    {
        Clean(html).ShouldNotContain(forbidden);
    }

    [Fact]
    public void Saved_checkboxes_are_read_only()
    {
        Clean("<input type=\"checkbox\">").ShouldContain("disabled");
    }
}
