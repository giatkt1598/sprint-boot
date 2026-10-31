using Shouldly;
using SprintBoot.Platform;
using Xunit;

namespace SprintBoot.UnitTests;

public class StorageOptionsTests
{
    [Fact]
    public void Local_provider_requires_a_path()
    {
        new StorageOptions().GetMissingKeys().ShouldBe(["Storage:Local:Path"]);
    }

    [Fact]
    public void S3_with_endpoint_does_not_require_region()
    {
        var options = new StorageOptions
        {
            Provider = "S3",
            S3 = new S3StorageOptions { Endpoint = "https://minio.test", Bucket = "b", AccessKey = "a", SecretKey = "s" },
        };

        options.GetMissingKeys().ShouldBeEmpty();
    }

    [Fact]
    public void S3_without_endpoint_requires_region_and_credentials()
    {
        var options = new StorageOptions { Provider = "S3" };

        options.GetMissingKeys().ShouldBe(
            ["Storage:S3:Bucket", "Storage:S3:AccessKey", "Storage:S3:SecretKey", "Storage:S3:Region"],
            ignoreOrder: true);
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        Should.Throw<InvalidOperationException>(() => new StorageOptions { Provider = "Ftp" }.GetMissingKeys());
    }
}
