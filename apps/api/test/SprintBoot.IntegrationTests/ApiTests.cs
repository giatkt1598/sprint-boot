using System.Net;
using System.Net.Http.Json;
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
}
