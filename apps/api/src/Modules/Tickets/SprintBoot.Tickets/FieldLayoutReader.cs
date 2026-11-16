using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

/// <summary>
/// Reads how a space lays out a ticket's Details sidebar. The ticket comes with it, so the sidebar shows the
/// fields in the space's order from the first render; the layout endpoint serves Space settings.
/// </summary>
public class FieldLayoutReader(
    IRepository<CustomField, Guid> fields,
    IRepository<ProjectCustomField, Guid> spaceFields,
    IRepository<ProjectFieldLayout, Guid> layouts) : ITransientDependency
{
    public async Task<FieldLayoutDto> ReadAsync(Guid projectId)
    {
        var layout = await layouts.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        var known = TicketDetailFields.All.Concat(await CustomKeysAsync(projectId)).ToList();
        // Fields the stored order does not know yet (a new field, a new built-in) go after the others.
        var order = (layout?.Order ?? []).Where(known.Contains).Distinct().ToList();
        order.AddRange(known.Where(key => !order.Contains(key)));
        return new FieldLayoutDto
        {
            Order = [.. order],
            Hidden = [.. (layout?.Hidden ?? []).Where(key => TicketDetailFields.All.Contains(key) && !TicketDetailFields.AlwaysShown.Contains(key))],
        };
    }

    /// <summary>The fields on in the space as "cf." keys, in their order.</summary>
    public async Task<List<string>> CustomKeysAsync(Guid projectId)
    {
        var rows = await spaceFields.GetListAsync(row => row.ProjectId == projectId);
        var ids = rows.Select(row => row.FieldId).ToList();
        var keys = (await fields.GetListAsync(field => ids.Contains(field.Id))).ToDictionary(field => field.Id, field => field.Key);
        return [.. rows.OrderBy(row => row.Order).Where(row => keys.ContainsKey(row.FieldId)).Select(row => CustomFieldConsts.KeyPrefix + keys[row.FieldId])];
    }
}
