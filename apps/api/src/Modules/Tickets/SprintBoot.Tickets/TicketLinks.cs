using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace SprintBoot.Tickets;

/// <summary>Shared by the whole workspace, so a link reads the same in every space.</summary>
public class TicketLinkType : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; set; } = null!;

    public string Outward { get; set; } = null!;

    public string Inward { get; set; } = null!;

    protected TicketLinkType()
    {
    }

    public TicketLinkType(Guid id, string name, string outward, string inward)
        : base(id)
    {
        Name = name;
        Outward = outward;
        Inward = inward;
    }
}

/// <summary>Stored once, from source to target; each side reads its own phrase.</summary>
public class TicketLink : CreationAuditedEntity<Guid>
{
    public Guid LinkTypeId { get; private set; }

    public Guid SourceTicketId { get; private set; }

    public Guid TargetTicketId { get; private set; }

    protected TicketLink()
    {
    }

    public TicketLink(Guid id, Guid linkTypeId, Guid sourceTicketId, Guid targetTicketId)
        : base(id)
    {
        LinkTypeId = linkTypeId;
        SourceTicketId = sourceTicketId;
        TargetTicketId = targetTicketId;
    }
}

public class TicketLinkTypesDataSeedContributor(IRepository<TicketLinkType, Guid> types, IGuidGenerator guids)
    : IDataSeedContributor, Volo.Abp.DependencyInjection.ITransientDependency
{
    /// <summary>The usual set, so a workspace can link tickets from day one; admins may rename or remove them.</summary>
    public static readonly (string Name, string Outward, string Inward)[] Defaults =
    [
        ("Blocks", "blocks", "is blocked by"),
        ("Relates", "relates to", "relates to"),
        ("Duplicate", "duplicates", "is duplicated by"),
        ("Cloners", "clones", "is cloned by"),
        ("Causes", "causes", "is caused by"),
    ];

    public async Task SeedAsync(DataSeedContext context)
    {
        if (await types.AnyAsync())
        {
            return;
        }

        await types.InsertManyAsync(Defaults.Select(item => new TicketLinkType(guids.Create(), item.Name, item.Outward, item.Inward)));
    }
}

[Authorize]
public class TicketLinkTypeAppService(IRepository<TicketLinkType, Guid> types, IRepository<TicketLink, Guid> links)
    : ApplicationService, ITicketLinkTypeAppService
{
    public async Task<List<TicketLinkTypeDto>> GetListAsync()
    {
        var list = await types.GetListAsync();
        var counts = (await links.GetListAsync()).GroupBy(link => link.LinkTypeId).ToDictionary(group => group.Key, group => group.Count());
        return [.. list.OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).Select(type => ToDto(type, counts.GetValueOrDefault(type.Id)))];
    }

    [Authorize(SprintBootPermissions.ManageTicketLinkTypes)]
    public async Task<TicketLinkTypeDto> CreateAsync(SaveTicketLinkTypeInput input)
    {
        var type = new TicketLinkType(GuidGenerator.Create(), string.Empty, string.Empty, string.Empty);
        await ApplyAsync(type, input);
        await types.InsertAsync(type, autoSave: true);
        return ToDto(type, 0);
    }

    [Authorize(SprintBootPermissions.ManageTicketLinkTypes)]
    public async Task<TicketLinkTypeDto> UpdateAsync(Guid id, SaveTicketLinkTypeInput input)
    {
        var type = await types.GetAsync(id);
        await ApplyAsync(type, input);
        await types.UpdateAsync(type, autoSave: true);
        return ToDto(type, await links.CountAsync(link => link.LinkTypeId == id));
    }

    [Authorize(SprintBootPermissions.ManageTicketLinkTypes)]
    public async Task DeleteAsync(Guid id)
    {
        var type = await types.GetAsync(id);
        if (await links.AnyAsync(link => link.LinkTypeId == id))
        {
            throw new BusinessException(TicketLinkErrors.TypeInUse);
        }

        await types.DeleteAsync(type);
    }

    private async Task ApplyAsync(TicketLinkType type, SaveTicketLinkTypeInput input)
    {
        var name = input.Name.Trim();
        var outward = input.Outward.Trim();
        var inward = input.Inward.Trim();
        if (name.Length == 0 || outward.Length == 0 || inward.Length == 0)
        {
            throw new BusinessException(TicketErrors.InvalidFieldValue);
        }

        var lower = name.ToLower();
        if (await types.AnyAsync(other => other.Id != type.Id && other.Name.ToLower() == lower))
        {
            throw new BusinessException(TicketLinkErrors.TypeNameTaken);
        }

        type.Name = name;
        type.Outward = outward;
        type.Inward = inward;
    }

    private static TicketLinkTypeDto ToDto(TicketLinkType type, int count) => new()
    {
        Id = type.Id,
        Name = type.Name,
        Outward = type.Outward,
        Inward = type.Inward,
        LinkCount = count,
    };
}

[Authorize]
public partial class TicketLinkAppService(
    IRepository<TicketLink, Guid> links,
    IRepository<TicketLinkType, Guid> types,
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketType, Guid> ticketTypes,
    IRepository<TicketActivity, Guid> activities,
    IProjectAccess projectAccess,
    IStatusLookup statusLookup,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock)
    : ApplicationService, ITicketLinkAppService
{
    [GeneratedRegex("^([A-Z][A-Z0-9]{1,9})-([0-9]+)$")]
    private static partial Regex KeyPattern();

    // A pasted ticket address: /browse/KEY-12, or the older /tickets and /issues.
    [GeneratedRegex(@"/(?:browse|tickets|issues)/([A-Za-z][A-Za-z0-9]*-[0-9]+)(?:[/?#]|$)")]
    private static partial Regex AddressPattern();

    /// <summary>The key typed, or the key in a pasted ticket address.</summary>
    public static string KeyOf(string text)
    {
        var trimmed = text.Trim();
        var address = AddressPattern().Match(trimmed);
        return (address.Success ? address.Groups[1].Value : trimmed).ToUpperInvariant();
    }

    public async Task<List<TicketLinkDto>> GetListAsync(Guid ticketId)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        var list = await links.GetListAsync(link => link.SourceTicketId == ticketId || link.TargetTicketId == ticketId);
        return await ToDtosAsync(ticketId, list);
    }

    public async Task<TicketLinkDto> CreateAsync(Guid ticketId, CreateTicketLinkInput input)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.EditTickets);
        var type = await types.GetAsync(input.LinkTypeId);

        // Another space is fine, but only one the caller can open: otherwise its keys would leak through errors.
        var other = await FindByKeyAsync(KeyOf(input.TicketKey))
            ?? throw new EntityNotFoundException(typeof(Ticket), input.TicketKey);
        if (!await projectAccess.IsGrantedAsync(other.ProjectId, ProjectPermissions.ViewTickets))
        {
            throw new EntityNotFoundException(typeof(Ticket), input.TicketKey);
        }

        // A link shows on both tickets, so it would change a read-only one.
        await EnsureNotArchivedAsync([other.ProjectId]);

        if (other.Id == ticket.Id)
        {
            throw new BusinessException(TicketLinkErrors.Self);
        }

        var (source, target) = input.Direction == TicketLinkDirection.Inward ? (other, ticket) : (ticket, other);
        if (await links.AnyAsync(link => link.LinkTypeId == type.Id && link.SourceTicketId == source.Id && link.TargetTicketId == target.Id))
        {
            throw new BusinessException(TicketLinkErrors.Duplicate);
        }

        var link = await links.InsertAsync(new TicketLink(GuidGenerator.Create(), type.Id, source.Id, target.Id), autoSave: true);
        await RecordAsync(link, type, null, added: true);
        return (await ToDtosAsync(ticket.Id, [link])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var link = await links.GetAsync(id);
        var sides = await tickets.GetListAsync(item => item.Id == link.SourceTicketId || item.Id == link.TargetTicketId);
        await EnsureNotArchivedAsync(sides.Select(side => side.ProjectId));
        var allowed = false;
        foreach (var side in sides)
        {
            allowed |= await projectAccess.IsGrantedAsync(side.ProjectId, ProjectPermissions.EditTickets);
        }

        if (!allowed)
        {
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Missing permission to edit either linked ticket");
        }

        var type = await types.GetAsync(link.LinkTypeId);
        await links.DeleteAsync(link);
        await RecordAsync(link, type, sides, added: false);
    }

    private async Task EnsureNotArchivedAsync(IEnumerable<Guid> projectIds)
    {
        foreach (var space in (await projectAccess.GetRefsAsync(projectIds)).Values.Where(space => space.IsArchived))
        {
            throw new BusinessException(ProjectErrors.Archived).WithData("space", space.Key);
        }
    }

    /// <summary>Both tickets get a history line, each in its own words: "blocks SB-2" and "is blocked by SB-1".</summary>
    private async Task RecordAsync(TicketLink link, TicketLinkType type, List<Ticket>? sides, bool added)
    {
        sides ??= await tickets.GetListAsync(item => item.Id == link.SourceTicketId || item.Id == link.TargetTicketId);
        var keys = await KeysAsync(sides);
        foreach (var side in sides)
        {
            var outward = side.Id == link.SourceTicketId;
            var otherId = outward ? link.TargetTicketId : link.SourceTicketId;
            if (!keys.TryGetValue(otherId, out var otherKey))
            {
                continue;
            }

            var text = $"{(outward ? type.Outward : type.Inward)} {otherKey}";
            await activities.InsertAsync(new TicketActivity(
                GuidGenerator.Create(), side.Id, currentActor.Actor, TicketFieldKeys.Link, added ? null : text, added ? text : null, clock.Now));
        }
    }

    private async Task<Dictionary<Guid, string>> KeysAsync(IReadOnlyCollection<Ticket> list)
    {
        var projects = await projectAccess.GetRefsAsync(list.Select(item => item.ProjectId).Distinct());
        return list.ToDictionary(item => item.Id, item => $"{projects[item.ProjectId].Key}-{item.Number}");
    }

    private async Task<List<TicketLinkDto>> ToDtosAsync(Guid ticketId, List<TicketLink> list)
    {
        var otherIds = list.Select(link => link.SourceTicketId == ticketId ? link.TargetTicketId : link.SourceTicketId).Distinct().ToList();
        // Deleted tickets drop out here, and so do their links.
        var others = (await tickets.GetListAsync(item => otherIds.Contains(item.Id))).ToDictionary(item => item.Id);
        var typeMap = (await types.GetListAsync()).ToDictionary(type => type.Id);

        var visible = new HashSet<Guid>();
        foreach (var projectId in others.Values.Select(item => item.ProjectId).Distinct())
        {
            if (await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.ViewTickets))
            {
                visible.Add(projectId);
            }
        }

        var shown = others.Values.Where(item => visible.Contains(item.ProjectId)).ToList();
        var keys = await KeysAsync(shown);
        var typeIds = shown.Select(item => item.TypeId).Distinct().ToList();
        var icons = (await ticketTypes.GetListAsync(type => typeIds.Contains(type.Id))).ToDictionary(type => type.Id, type => type.Icon);
        var statuses = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var users = await userDirectory.GetByIdsAsync(shown.Where(item => item.AssigneeId.HasValue).Select(item => item.AssigneeId!.Value).Distinct());

        return
        [
            .. list
                .Where(link => others.ContainsKey(link.SourceTicketId == ticketId ? link.TargetTicketId : link.SourceTicketId))
                .Select(link =>
                {
                    var outward = link.SourceTicketId == ticketId;
                    var other = others[outward ? link.TargetTicketId : link.SourceTicketId];
                    var type = typeMap[link.LinkTypeId];
                    return new TicketLinkDto
                    {
                        Id = link.Id,
                        LinkTypeId = type.Id,
                        Direction = outward ? TicketLinkDirection.Outward : TicketLinkDirection.Inward,
                        Phrase = outward ? type.Outward : type.Inward,
                        Ticket = !visible.Contains(other.ProjectId) ? null : new TicketRefDto
                        {
                            Id = other.Id,
                            Key = keys[other.Id],
                            Title = other.Title,
                            TypeIcon = icons[other.TypeId],
                            Status = statuses[other.StatusId],
                            Assignee = other.AssigneeId is { } assignee ? users.GetValueOrDefault(assignee) : null,
                            ConcurrencyStamp = other.ConcurrencyStamp,
                        },
                    };
                })
                .OrderBy(dto => dto.Phrase)
                .ThenBy(dto => dto.Ticket?.Key),
        ];
    }

    private async Task<Ticket?> FindByKeyAsync(string key)
    {
        var match = KeyPattern().Match(key);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var number))
        {
            return null;
        }

        var project = await projectAccess.FindRefByKeyAsync(match.Groups[1].Value);
        return project is null ? null : await tickets.FirstOrDefaultAsync(item => item.ProjectId == project.Id && item.Number == number);
    }
}
