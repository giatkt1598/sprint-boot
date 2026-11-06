using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.Issues;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Aws;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>LocalStack's S3, which checks presigned signatures and expiry like the real service.</summary>
public sealed class S3Fixture : IAsyncLifetime
{
    public const string Bucket = "sprint-boot";
    public const string AccessKey = "test";
    public const string SecretKey = "test";

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("localstack/localstack:4")
        .WithEnvironment("SERVICES", "s3")
        // Presigned URLs are checked as on AWS, expiry included.
        .WithEnvironment("S3_SKIP_SIGNATURE_VALIDATION", "0")
        .WithPortBinding(4566, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready."))
        .Build();

    public string Endpoint => $"http://localhost:{_container.GetMappedPublicPort(4566)}";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        using var client = new AmazonS3Client(AccessKey, SecretKey,
            new AmazonS3Config { ServiceURL = Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await client.PutBucketAsync(Bucket);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>Against a real S3-compatible service: the browser's part PUTs go to presigned URLs, never through the API.</summary>
[Collection(ApiCollection.Name)]
public class S3UploadStorageTests(ApiFixture fixture, S3Fixture s3) : IClassFixture<S3Fixture>
{
    private const long MinimumPart = 5 * 1024 * 1024;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class FixedContainerConfiguration(BlobContainerConfiguration configuration) : IBlobContainerConfigurationProvider
    {
        public BlobContainerConfiguration Get(string name) => configuration;
    }

    private (S3UploadStorage Storage, IAmazonS3ClientFactory Clients, BlobContainerConfiguration Configuration) Create(IServiceProvider services)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:Endpoint"] = s3.Endpoint,
            ["Storage:S3:Bucket"] = S3Fixture.Bucket,
        }).Build();
        var configuration = new BlobContainerConfiguration().UseAws(aws =>
        {
            aws.AccessKeyId = S3Fixture.AccessKey;
            aws.SecretAccessKey = S3Fixture.SecretKey;
            aws.ContainerName = S3Fixture.Bucket;
        });
        var clients = new EndpointAmazonS3ClientFactory(settings);
        var storage = new S3UploadStorage(
            new FixedContainerConfiguration(configuration),
            services.GetRequiredService<IBlobNormalizeNamingService>(),
            services.GetRequiredService<IAwsBlobNameCalculator>(),
            clients,
            settings);
        return (storage, clients, configuration);
    }

    private static UploadSession Session(long size) => new(
        Guid.NewGuid(), UploadPurpose.Attachment, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "big.bin", "application/octet-stream",
        size, MinimumPart, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

    [Fact]
    public async Task Parts_put_to_presigned_urls_join_into_the_blob_the_container_reads()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (storage, clients, configuration) = Create(scope.ServiceProvider);
        var file = new byte[MinimumPart + 10];
        Random.Shared.NextBytes(file);
        var session = Session(file.Length);
        session.StorageUploadId = await storage.BeginAsync(session);

        using var http = new HttpClient();
        var parts = new List<UploadedPartDto>();
        for (var number = 1; number <= session.PartCount; number++)
        {
            var url = storage.PartUrl(session, number, DateTime.UtcNow.AddMinutes(10))!;
            var start = (int)((number - 1) * session.PartSize);
            var response = await http.PutAsync(url, new ByteArrayContent(file[start..(int)Math.Min(file.Length, start + session.PartSize)]), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
            parts.Add(new UploadedPartDto { Number = number, ETag = response.Headers.ETag!.Tag });
        }

        (await storage.CompleteAsync(session, parts)).ShouldBe(file.Length);

        // Stored where the blob container looks for it, so downloads work as for any other attachment.
        using var client = await clients.GetAmazonS3Client(configuration.GetAwsConfiguration());
        using var stored = await client.GetObjectAsync(S3Fixture.Bucket, $"host/{session.StorageKey}", Token);
        using var copy = new MemoryStream();
        await stored.ResponseStream.CopyToAsync(copy, Token);
        copy.ToArray().ShouldBe(file);
    }

    [Fact]
    public async Task An_expired_url_is_refused_and_a_wrong_etag_does_not_complete()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var (storage, _, _) = Create(scope.ServiceProvider);
        var session = Session(10);
        session.StorageUploadId = await storage.BeginAsync(session);

        using var http = new HttpClient();
        var expired = await http.PutAsync(storage.PartUrl(session, 1, DateTime.UtcNow.AddSeconds(-30)), new ByteArrayContent(new byte[10]), Token);
        expired.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var sent = await http.PutAsync(storage.PartUrl(session, 1, DateTime.UtcNow.AddMinutes(10)), new ByteArrayContent(new byte[10]), Token);
        sent.EnsureSuccessStatusCode();
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => storage.CompleteAsync(session, [new UploadedPartDto { Number = 1, ETag = "\"wrong\"" }]));

        await storage.AbortAsync(session);
        await storage.AbortAsync(session);
    }
}
