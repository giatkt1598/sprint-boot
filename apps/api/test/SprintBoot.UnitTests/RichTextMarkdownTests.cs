using ModelContextProtocol;
using Shouldly;
using SprintBoot.Host.Mcp;
using SprintBoot.Issues;
using Xunit;

namespace SprintBoot.UnitTests;

public class RichTextMarkdownTests
{
    private static readonly Guid Ann = Guid.Parse("0b9a3f44-58a5-4c3e-9a4f-3f1f2d7f6a11");

    private static string Html(string markdown) => RichTextMarkdown.ToHtmlAsync(markdown, Resolve).GetAwaiter().GetResult();

    private static Task<MentionTarget?> Resolve(string text) =>
        Task.FromResult(text == "ann@example.com" ? new MentionTarget(Ann, "Ann Lee") : null);

    private static string Clean(string html) => new RichTextSanitizer().Clean(html).Html;

    [Theory]
    [InlineData("# Title", "<h1>Title</h1>")]
    [InlineData("#### Deep", "<h3>Deep</h3>")]
    [InlineData("**bold** *it* ~~gone~~ `code` ==mark==", "<strong>bold</strong> <em>it</em> <del>gone</del> <code>code</code> <mark>mark</mark>")]
    [InlineData("one\ntwo", "<p>one<br>two</p>")]
    [InlineData("```js\nlet a = 1;\n```", "<pre><code class=\"language-js\">let a = 1;</code></pre>")]
    [InlineData("> quote", "<blockquote><p>quote</p></blockquote>")]
    [InlineData("- a\n  - b\n- c", "<ul><li>a<ul><li>b</li></ul></li><li>c</li></ul>")]
    [InlineData("- [ ] a\n  - [x] b","<div><p>a</p><ul data-type=\"taskList\"><li data-type=\"taskItem\" data-checked=\"true\">")]
    [InlineData("[site](https://x.test)", "<a href=\"https://x.test\">site</a>")]
    public void Markdown_becomes_editor_html(string markdown, string expected)
    {
        Html(markdown).ShouldContain(expected);
    }

    [Fact]
    public void Task_lists_take_the_editor_shape()
    {
        var html = Html("- [x] done\n- [ ] todo");

        html.ShouldContain("<ul data-type=\"taskList\">");
        html.ShouldContain("data-type=\"taskItem\" data-checked=\"true\"");
        html.ShouldContain("<div><p>done</p></div>");
        html.ShouldContain("data-checked=\"false\"");
        html.ShouldNotContain("contains-task-list");
    }

    [Fact]
    public void Callouts_and_mentions_take_the_editor_shape()
    {
        Html(":::info\nCareful\n:::").ShouldContain("<div class=\"callout\" data-type=\"callout\"><p>Careful</p></div>");

        var html = Html("Ask @[Ann](user:ann@example.com) and @[Bob](user:6f1c1f5a-38a5-4f3e-9b0f-1b1f2d7f6a22)");
        html.ShouldContain($"data-type=\"mention\" class=\"mention\" data-id=\"{Ann}\" data-label=\"Ann\">@Ann</span>");
        html.ShouldContain("data-id=\"6f1c1f5a-38a5-4f3e-9b0f-1b1f2d7f6a22\"");
        html.ShouldNotContain("@@");
    }

    [Fact]
    public void An_unknown_mention_is_refused()
    {
        Should.Throw<McpException>(() => Html("@[X](user:nobody@example.com)"));
    }

    [Fact]
    public void Tables_become_html_tables()
    {
        var html = Html("| a | b |\n| --- | --- |\n| 1 | 2 |");

        html.ShouldContain("<th>a</th>");
        html.ShouldContain("<td>2</td>");
    }

    [Theory]
    [InlineData("<h2>Title</h2>", "## Title")]
    [InlineData("<p><strong>a</strong> <em>b</em> <s>c</s> <code>d</code></p>", "**a** *b* ~~c~~ `d`")]
    [InlineData("<p><strong>a </strong>b</p>", "**a** b")]
    [InlineData("<p>one<br>two</p>", "one\ntwo")]
    [InlineData("<ul><li><p>a</p><ul><li><p>b</p></li></ul></li><li><p>c</p></li></ul>", "- a\n  - b\n- c")]
    [InlineData("<ol start=\"3\"><li><p>a</p></li><li><p>b</p></li></ol>", "3. a\n4. b")]
    [InlineData("<pre><code class=\"language-csharp\">var a = 1;</code></pre>", "```csharp\nvar a = 1;\n```")]
    [InlineData("<blockquote><p>a</p><p>b</p></blockquote>", "> a\n>\n> b")]
    [InlineData("<hr>", "---")]
    [InlineData("<p><a href=\"https://x.test\" target=\"_blank\">site</a></p>", "[site](https://x.test)")]
    [InlineData("<p><span data-type=\"mention\" class=\"mention\" data-id=\"1\" data-label=\"Ann\">@Ann</span></p>", "@[Ann](user:1)")]
    [InlineData("<div data-type=\"callout\" class=\"callout\"><p>a</p></div>", ":::callout\na\n:::")]
    [InlineData("<p><span style=\"color: #B91C1C\">red <strong>bold</strong></span></p>", "<span style=\"color: #B91C1C\">red **bold**</span>")]
    [InlineData("<p style=\"text-align: center\">mid</p>", "<p style=\"text-align: center\">mid</p>")]
    [InlineData("<p>1. not a list, a*b, snake_case, _edge_</p>", "1\\. not a list, a\\*b, snake_case, \\_edge\\_")]
    public void Editor_html_becomes_markdown(string html, string expected)
    {
        RichTextMarkdown.ToMarkdown(html).ShouldBe(expected);
    }

    [Fact]
    public void Task_lists_and_tables_become_markdown()
    {
        RichTextMarkdown.ToMarkdown(
            "<ul data-type=\"taskList\"><li data-checked=\"true\" data-type=\"taskItem\"><label><input type=\"checkbox\" checked=\"checked\"><span></span></label><div><p>done</p></div></li>" +
            "<li data-checked=\"false\" data-type=\"taskItem\"><label><input type=\"checkbox\"><span></span></label><div><p>todo</p></div></li></ul>")
            .ShouldBe("- [x] done\n- [ ] todo");

        RichTextMarkdown.ToMarkdown("<table><tbody><tr><th><p>a</p></th><th><p>b</p></th></tr><tr><td><p>1</p></td><td><p>x|y</p></td></tr></tbody></table>")
            .ShouldBe("| a | b |\n| --- | --- |\n| 1 | x\\|y |");
    }

    [Theory]
    [InlineData("<h2>Plan</h2><p>Ship <strong>it</strong> soon</p><ul><li><p>one</p></li><li><p>two</p></li></ul>")]
    [InlineData("<ul data-type=\"taskList\"><li data-type=\"taskItem\" data-checked=\"true\"><label><input type=\"checkbox\" checked=\"checked\"><span></span></label><div><p>done</p></div></li></ul>")]
    [InlineData("<div data-type=\"callout\" class=\"callout\"><p>Careful</p></div>")]
    [InlineData("<p><span style=\"color: #B91C1C\">red</span> and <mark data-color=\"#FEE2E2\" style=\"background-color: #FEE2E2; color: inherit\">marked</mark></p>")]
    [InlineData("<pre><code class=\"language-sql\">select * from issues where a_b = 1;</code></pre>")]
    [InlineData("<p>Ask <span data-type=\"mention\" class=\"mention\" data-id=\"0b9a3f44-58a5-4c3e-9a4f-3f1f2d7f6a11\" data-label=\"Ann\">@Ann</span></p>")]
    [InlineData("<p>Costs 5 * 3 &lt;b&gt; [x](y) a_b</p>")]
    public void Editor_html_survives_a_round_trip(string html)
    {
        // Stored text has always been through the sanitizer, which also rewrites colors.
        html = Clean(html);
        var markdown = RichTextMarkdown.ToMarkdown(html);
        var back = Html(markdown);

        RichTextMarkdown.ToMarkdown(Clean(back)).ShouldBe(markdown);
        Normalize(Clean(back)).ShouldBe(Normalize(html));
    }

    [Fact]
    public void Empty_input_stays_empty()
    {
        Html("  ").ShouldBe(string.Empty);
        RichTextMarkdown.ToMarkdown(null).ShouldBe(string.Empty);
    }

    // Attribute order and the disabled flag on checkboxes do not matter to the editor.
    private static string Normalize(string html) =>
        new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html).Body!.TextContent.Replace(" ", string.Empty);
}
