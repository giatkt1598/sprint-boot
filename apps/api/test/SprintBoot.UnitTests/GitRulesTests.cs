using Shouldly;
using SprintBoot.GitIntegration;
using Xunit;

namespace SprintBoot.UnitTests;

public class GitRulesTests
{
    private static readonly string[] Keys = ["SB", "OPS"];

    [Theory]
    [InlineData("SB-12 fix", false, "SB-12")]
    [InlineData("touches SB-1, OPS-2", false, "SB-1,OPS-2")]
    [InlineData("sb-12 lower case prose", false, "")]
    [InlineData("feature/sb-12-login", true, "SB-12")]
    [InlineData("XSB-12 and SB-12a and ABC-1", false, "")]
    [InlineData("SB-0", false, "")]
    public void Scanner_finds_only_known_keys(string text, bool ignoreCase, string expected)
    {
        var found = IssueKeyScanner.Scan(text, Keys, ignoreCase);

        found.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, "SB-123", "Sửa lỗi đăng nhập!", "feature/SB-123-sua-loi-dang-nhap")]
    [InlineData("bug/{key}", "SB-1", "Anything", "bug/SB-1")]
    [InlineData("feature/{issue_key}", "SB-1", "Anything", "feature/SB-1")]
    public void Branch_names_follow_the_template(string? template, string key, string title, string expected)
    {
        IssueKeyScanner.BranchName(template, key, title).ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://git.example.com", "https://git.example.com", null)]
    [InlineData("https://git.example.com/acme/", "https://git.example.com", "acme")]
    public void Addresses_split_into_server_and_organization(string url, string server, string? owner)
    {
        GitUrls.Split(url).ShouldBe((server, owner));
    }

    [Fact]
    public void Signature_comparison_rejects_tampering()
    {
        var signature = GitHttp.Hex(System.Security.Cryptography.HMACSHA256.HashData("secret"u8, "body"u8));

        GitHttp.SignatureMatches("body", "secret", signature).ShouldBeTrue();
        GitHttp.SignatureMatches("body2", "secret", signature).ShouldBeFalse();
        GitHttp.SignatureMatches("body", "secret", null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("queued", null, GitCiState.Pending)]
    [InlineData("in_progress", null, GitCiState.Running)]
    [InlineData("completed", "success", GitCiState.Success)]
    [InlineData("completed", "failure", GitCiState.Failure)]
    public void GitHub_check_runs_map_to_ci_states(string status, string? conclusion, GitCiState expected)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            repository = new { id = 1, full_name = "a/b", html_url = "http://x" },
            check_run = new { head_sha = "abc", name = "build", status, conclusion, html_url = "http://x/1" },
        });

        var parsed = new GitHubProvider(null!, null!).Parse("check_run", payload);

        parsed.ShouldBeOfType<GitStatusEvent>().State.ShouldBe(expected);
    }
}
