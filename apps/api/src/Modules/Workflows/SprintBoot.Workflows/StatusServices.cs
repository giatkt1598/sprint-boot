using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Workflows;

[Authorize]
public class StatusAppService(IRepository<Status, Guid> statuses) : ApplicationService, IStatusAppService
{
    public async Task<List<StatusDto>> GetListAsync()
    {
        var items = await statuses.GetListAsync();
        return [.. items.OrderBy(status => status.Order).Select(StatusMapping.ToDto)];
    }
}

public class StatusLookup(IRepository<Status, Guid> statuses) : IStatusLookup, ITransientDependency
{
    public async Task<StatusDto> GetInitialAsync() =>
        StatusMapping.ToDto(await statuses.GetAsync(WorkflowsDataSeedContributor.ToDoId));

    public async Task<List<StatusDto>> GetListAsync()
    {
        var items = await statuses.GetListAsync();
        return [.. items.OrderBy(status => status.Order).Select(StatusMapping.ToDto)];
    }
}

public static class StatusMapping
{
    public static StatusDto ToDto(Status status) => new()
    {
        Id = status.Id,
        Name = status.Name,
        Category = status.Category,
    };
}

public class WorkflowsDataSeedContributor(IRepository<Status, Guid> statuses) : IDataSeedContributor, ITransientDependency
{
    public static readonly Guid ToDoId = new("6f1b1a10-0000-4000-8000-000000000001");
    public static readonly Guid InProgressId = new("6f1b1a10-0000-4000-8000-000000000002");
    public static readonly Guid DoneId = new("6f1b1a10-0000-4000-8000-000000000003");

    public async Task SeedAsync(DataSeedContext context)
    {
        if (await statuses.AnyAsync())
        {
            return;
        }

        await statuses.InsertManyAsync(
        [
            new Status(ToDoId, "To Do", StatusCategory.ToDo, 0),
            new Status(InProgressId, "In Progress", StatusCategory.InProgress, 1),
            new Status(DoneId, "Done", StatusCategory.Done, 2),
        ]);
    }
}

public static class WorkflowsModelBuilderExtensions
{
    public const string Schema = "workflows";

    public static void ConfigureWorkflows(this ModelBuilder builder)
    {
        builder.Entity<Status>(b =>
        {
            b.ToTable("statuses", Schema);
            b.Property(status => status.Name).HasMaxLength(64).IsRequired();
        });
    }
}
