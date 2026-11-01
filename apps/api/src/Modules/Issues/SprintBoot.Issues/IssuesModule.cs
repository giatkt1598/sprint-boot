using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SprintBoot.Platform;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Aws;
using Volo.Abp.BlobStoring.FileSystem;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace SprintBoot.Issues;

[DependsOn(
    typeof(IssuesContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpBlobStoringFileSystemModule),
    typeof(AbpBlobStoringAwsModule),
    typeof(AbpBackgroundWorkersModule))]
public class IssuesModule : AbpModule
{
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context) =>
        context.AddBackgroundWorkerAsync<RankRebalanceWorker>();

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var storage = context.Services.GetConfiguration().GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? new StorageOptions();

        Configure<AbpBlobStoringOptions>(options => options.Containers.ConfigureDefault(container =>
        {
            if (storage.Provider == "S3")
            {
                container.UseAws(aws =>
                {
                    aws.AccessKeyId = storage.S3.AccessKey;
                    aws.SecretAccessKey = storage.S3.SecretKey;
                    aws.Region = string.IsNullOrEmpty(storage.S3.Region) ? null : storage.S3.Region;
                    aws.ContainerName = storage.S3.Bucket;
                    aws.CreateContainerIfNotExists = false;
                });
            }
            else
            {
                container.UseFileSystem(fileSystem => fileSystem.BasePath = Path.GetFullPath(storage.Local.Path));
            }
        }));
    }
}
