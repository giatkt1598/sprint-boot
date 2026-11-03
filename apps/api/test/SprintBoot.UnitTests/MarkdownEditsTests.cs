using ModelContextProtocol;
using Shouldly;
using SprintBoot.Host.Mcp;
using Xunit;

namespace SprintBoot.UnitTests;

public class MarkdownEditsTests
{
    private const string Document = "Intro\n\n## Goals\n\nShip it\n\n### Detail\n\nMore\n\n## Risks\n\nNone\n\n```\n## Not a heading\n```";

    private static string Apply(params DescriptionEdit[] edits) => MarkdownEdits.Apply(Document, edits);

    [Fact]
    public void Replace_changes_exactly_one_match()
    {
        Apply(new DescriptionEdit { Op = "replace", Find = "Ship it", Text = "Ship it today" }).ShouldContain("Ship it today\n");
    }

    [Fact]
    public void Replace_refuses_missing_or_ambiguous_text_unless_all()
    {
        Should.Throw<McpException>(() => Apply(new DescriptionEdit { Op = "replace", Find = "absent", Text = "x" }));
        Should.Throw<McpException>(() => Apply(new DescriptionEdit { Op = "replace", Find = "##", Text = "x" }));
        Apply(new DescriptionEdit { Op = "replace", Find = "o", Text = "0", All = true }).ShouldNotContain("o");
    }

    [Fact]
    public void Replace_section_keeps_the_heading_and_subsections_go_with_it()
    {
        var result = Apply(new DescriptionEdit { Op = "replace_section", Heading = "## goals", Text = "New goals" });

        result.ShouldContain("## Goals\n\nNew goals\n\n## Risks");
        result.ShouldNotContain("Detail");
    }

    [Fact]
    public void Insert_after_section_lands_before_the_next_heading()
    {
        Apply(new DescriptionEdit { Op = "insert_after_section", Heading = "Risks", Text = "## Notes\n\nAdded" })
            .ShouldEndWith("## Not a heading\n```\n\n## Notes\n\nAdded");
        Apply(new DescriptionEdit { Op = "insert_after_section", Heading = "Detail", Text = "Extra" })
            .ShouldContain("More\n\nExtra\n\n## Risks");
    }

    [Fact]
    public void Append_and_prepend_add_paragraphs_in_order()
    {
        var result = Apply(
            new DescriptionEdit { Op = "prepend", Text = "Top" },
            new DescriptionEdit { Op = "append", Text = "Bottom" });

        result.ShouldStartWith("Top\n\nIntro");
        result.ShouldEndWith("```\n\nBottom");
    }

    [Fact]
    public void Headings_inside_code_blocks_are_not_sections()
    {
        Should.Throw<McpException>(() => Apply(new DescriptionEdit { Op = "replace_section", Heading = "Not a heading", Text = "x" }))
            .Message.ShouldContain("Goals, Detail, Risks");
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("45m", 45)]
    [InlineData("1h 30m", 90)]
    [InlineData("1.5h", 90)]
    [InlineData("2h", 120)]
    public void Durations_are_read_in_minutes(string text, int minutes)
    {
        McpResolver.Minutes(text).ShouldBe(minutes);
    }

    [Fact]
    public void A_bad_duration_is_refused()
    {
        Should.Throw<McpException>(() => McpResolver.Minutes("soon"));
    }
}
