using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Shouldly;
using SprintBoot.Platform;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ping_is_served_under_the_versioned_prefix()
    {
        var ping = await fixture.Factory.CreateClient()
            .GetFromJsonAsync<SystemPingDto>("/api/v1/system/ping", Token);

        ping.ShouldNotBeNull();
        ping.Status.ShouldBe("ok");
    }

    [Fact]
    public async Task Ready_probe_reports_all_dependencies_healthy()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/health/ready", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_document_only_contains_versioned_paths()
    {
        var document = await fixture.Factory.CreateClient()
            .GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json", Token);

        var paths = document!.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();
        paths.ShouldContain("/api/v1/system/ping");
        paths.ShouldAllBe(path => path.StartsWith("/api/v1/"));
    }

    [Fact]
    public async Task OpenApi_document_matches_the_committed_client()
    {
        var served = await fixture.Factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json", Token);
        served = served.ReplaceLineEndings("\n").TrimEnd() + "\n";
        var committedPath = Path.GetFullPath(Path.Combine(
            ThisDirectory(), "..", "..", "..", "..", "packages", "api-client", "openapi.json"));

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(committedPath, served, Token);
        }

        var committed = (await File.ReadAllTextAsync(committedPath, Token)).ReplaceLineEndings("\n");
        committed.ShouldBe(served, "API contract changed: run pnpm gen:api and commit the result.");
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
