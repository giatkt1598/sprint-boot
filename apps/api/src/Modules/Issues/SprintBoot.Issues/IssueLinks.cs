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

namespace SprintBoot.Issues;

/// <summary>Shared by the whole workspace, so a link reads the same in every space.</summary>
public class IssueLinkType : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; set; } = null!;

    public string Outward { get; set; } = null!;

    public string Inward { get; set; } = null!;

    protected IssueLinkType()
    {
    }

    public IssueLinkType(Guid id, string name, string outward, string inward)
        : base(id)
    {
        Name = name;
        Outward = outward;
        Inward = inward;
    }
}

/// <summary>Stored once, from source to target; each side reads its own phrase.</summary>
public class IssueLink : CreationAuditedEntity<Guid>
{
    public Guid LinkTypeId { get; private set; }

    public Guid SourceIssueId { get; private set; }

    public Guid TargetIssueId { get; private set; }

    protected IssueLink()
    {
    }

    public IssueLink(Guid id, Guid linkTypeId, Guid sourceIssueId, Guid targetIssueId)
        : base(id)
    {
        LinkTypeId = linkTypeId;
        SourceIssueId = sourceIssueId;
        TargetIssueId = targetIssueId;
    }
}

public class IssueLinkTypesDataSeedContributor(IRepository<IssueLinkType, Guid> types, IGuidGenerator guids)
    : IDataSeedContributor, Volo.Abp.DependencyInjection.ITransientDependency
{
    /// <summary>The usual set, so a workspace can link issues from day one; admins may rename or remove them.</summary>
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

        await types.InsertManyAsync(Defaults.Select(item => new IssueLinkType(guids.Create(), item.Name, item.Outward, item.Inward)));
    }
}

[Authorize]
public class IssueLinkTypeAppService(IRepository<IssueLinkType, Guid> types, IRepository<IssueLink, Guid> links)
    : ApplicationService, IIssueLinkTypeAppService
{
    public async Task<List<IssueLinkTypeDto>> GetListAsync()
    {
        var list = await types.GetListAsync();
        var counts = (await links.GetListAsync()).GroupBy(link => link.LinkTypeId).ToDictionary(group => group.Key, group => group.Count());
        return [.. list.OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).Select(type => ToDto(type, counts.GetValueOrDefault(type.Id)))];
    }

    [Authorize(SprintBootPermissions.ManageIssueLinkTypes)]
    public async Task<IssueLinkTypeDto> CreateAsync(SaveIssueLinkTypeInput input)
    {
        var type = new IssueLinkType(GuidGenerator.Create(), string.Empty, string.Empty, string.Empty);
        await ApplyAsync(type, input);
        await types.InsertAsync(type, autoSave: true);
        return ToDto(type, 0);
    }

    [Authorize(SprintBootPermissions.ManageIssueLinkTypes)]
    public async Task<IssueLinkTypeDto> UpdateAsync(Guid id, SaveIssueLinkTypeInput input)
    {
        var type = await types.GetAsync(id);
        await ApplyAsync(type, input);
        await types.UpdateAsync(type, autoSave: true);
        return ToDto(type, await links.CountAsync(link => link.LinkTypeId == id));
    }

    [Authorize(SprintBootPermissions.ManageIssueLinkTypes)]
    public async Task DeleteAsync(Guid id)
    {
        var type = await types.GetAsync(id);
        if (await links.AnyAsync(link => link.LinkTypeId == id))
        {
            throw new BusinessException(IssueLinkErrors.TypeInUse);
        }

        await types.DeleteAsync(type);
    }

    private async Task ApplyAsync(IssueLinkType type, SaveIssueLinkTypeInput input)
    {
        var name = input.Name.Trim();
        var outward = input.Outward.Trim();
        var inward = input.Inward.Trim();
        if (name.Length == 0 || outward.Length == 0 || inward.Length == 0)
        {
            throw new BusinessException(IssueErrors.InvalidFieldValue);
        }

        var lower = name.ToLower();
        if (await types.AnyAsync(other => other.Id != type.Id && other.Name.ToLower() == lower))
        {
            throw new BusinessException(IssueLinkErrors.TypeNameTaken);
        }

        type.Name = name;
        type.Outward = outward;
        type.Inward = inward;
    }

    private static IssueLinkTypeDto ToDto(IssueLinkType type, int count) => new()
    {
        Id = type.Id,
        Name = type.Name,
        Outward = type.Outward,
        Inward = type.Inward,
        LinkCount = count,
    };
}

[Authorize]
public partial class IssueLinkAppService(
    IRepository<IssueLink, Guid> links,
    IRepository<IssueLinkType, Guid> types,
    IRepository<Issue, Guid> issues,
    IRepository<IssueType, Guid> issueTypes,
    IRepository<IssueActivity, Guid> activities,
    IProjectAccess projectAccess,
    IStatusLookup statusLookup,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock)
    : ApplicationService, IIssueLinkAppService
{
    [GeneratedRegex("^([A-Z][A-Z0-9]{1,9})-([0-9]+)$")]
    private static partial Regex KeyPattern();

    public async Task<List<IssueLinkDto>> GetListAsync(Guid issueId)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        var list = await links.GetListAsync(link => link.SourceIssueId == issueId || link.TargetIssueId == issueId);
        return await ToDtosAsync(issueId, list);
    }

    public async Task<IssueLinkDto> CreateAsync(Guid issueId, CreateIssueLinkInput input)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.EditIssues);
        var type = await types.GetAsync(input.LinkTypeId);

        // Another space is fine, but only one the caller can open: otherwise its keys would leak through errors.
        var other = await FindByKeyAsync(input.IssueKey.Trim().ToUpperInvariant())
            ?? throw new EntityNotFoundException(typeof(Issue), input.IssueKey);
        if (!await projectAccess.IsGrantedAsync(other.ProjectId, ProjectPermissions.ViewIssues))
        {
            throw new EntityNotFoundException(typeof(Issue), input.IssueKey);
        }

        if (other.Id == issue.Id)
        {
            throw new BusinessException(IssueLinkErrors.Self);
        }

        var (source, target) = input.Direction == IssueLinkDirection.Inward ? (other, issue) : (issue, other);
        if (await links.AnyAsync(link => link.LinkTypeId == type.Id && link.SourceIssueId == source.Id && link.TargetIssueId == target.Id))
        {
            throw new BusinessException(IssueLinkErrors.Duplicate);
        }

        var link = await links.InsertAsync(new IssueLink(GuidGenerator.Create(), type.Id, source.Id, target.Id), autoSave: true);
        await RecordAsync(link, type, null, added: true);
        return (await ToDtosAsync(issue.Id, [link])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var link = await links.GetAsync(id);
        var sides = await issues.GetListAsync(item => item.Id == link.SourceIssueId || item.Id == link.TargetIssueId);
        var allowed = false;
        foreach (var side in sides)
        {
            allowed |= await projectAccess.IsGrantedAsync(side.ProjectId, ProjectPermissions.EditIssues);
        }

        if (!allowed)
        {
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Missing permission to edit either linked issue");
        }

        var type = await types.GetAsync(link.LinkTypeId);
        await links.DeleteAsync(link);
        await RecordAsync(link, type, sides, added: false);
    }

    /// <summary>Both issues get a history line, each in its own words: "blocks SB-2" and "is blocked by SB-1".</summary>
    private async Task RecordAsync(IssueLink link, IssueLinkType type, List<Issue>? sides, bool added)
    {
        sides ??= await issues.GetListAsync(item => item.Id == link.SourceIssueId || item.Id == link.TargetIssueId);
        var keys = await KeysAsync(sides);
        foreach (var side in sides)
        {
            var outward = side.Id == link.SourceIssueId;
            var otherId = outward ? link.TargetIssueId : link.SourceIssueId;
            if (!keys.TryGetValue(otherId, out var otherKey))
            {
                continue;
            }

            var text = $"{(outward ? type.Outward : type.Inward)} {otherKey}";
            await activities.InsertAsync(new IssueActivity(
                GuidGenerator.Create(), side.Id, currentActor.Actor, IssueFieldKeys.Link, added ? null : text, added ? text : null, clock.Now));
        }
    }

    private async Task<Dictionary<Guid, string>> KeysAsync(IReadOnlyCollection<Issue> list)
    {
        var projects = await projectAccess.GetRefsAsync(list.Select(item => item.ProjectId).Distinct());
        return list.ToDictionary(item => item.Id, item => $"{projects[item.ProjectId].Key}-{item.Number}");
    }

    private async Task<List<IssueLinkDto>> ToDtosAsync(Guid issueId, List<IssueLink> list)
    {
        var otherIds = list.Select(link => link.SourceIssueId == issueId ? link.TargetIssueId : link.SourceIssueId).Distinct().ToList();
        // Deleted issues drop out here, and so do their links.
        var others = (await issues.GetListAsync(item => otherIds.Contains(item.Id))).ToDictionary(item => item.Id);
        var typeMap = (await types.GetListAsync()).ToDictionary(type => type.Id);

        var visible = new HashSet<Guid>();
        foreach (var projectId in others.Values.Select(item => item.ProjectId).Distinct())
        {
            if (await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.ViewIssues))
            {
                visible.Add(projectId);
            }
        }

        var shown = others.Values.Where(item => visible.Contains(item.ProjectId)).ToList();
        var keys = await KeysAsync(shown);
        var typeIds = shown.Select(item => item.TypeId).Distinct().ToList();
        var icons = (await issueTypes.GetListAsync(type => typeIds.Contains(type.Id))).ToDictionary(type => type.Id, type => type.Icon);
        var statuses = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var users = await userDirectory.GetByIdsAsync(shown.Where(item => item.AssigneeId.HasValue).Select(item => item.AssigneeId!.Value).Distinct());

        return
        [
            .. list
                .Where(link => others.ContainsKey(link.SourceIssueId == issueId ? link.TargetIssueId : link.SourceIssueId))
                .Select(link =>
                {
                    var outward = link.SourceIssueId == issueId;
                    var other = others[outward ? link.TargetIssueId : link.SourceIssueId];
                    var type = typeMap[link.LinkTypeId];
                    return new IssueLinkDto
                    {
                        Id = link.Id,
                        LinkTypeId = type.Id,
                        Direction = outward ? IssueLinkDirection.Outward : IssueLinkDirection.Inward,
                        Phrase = outward ? type.Outward : type.Inward,
                        Issue = !visible.Contains(other.ProjectId) ? null : new IssueRefDto
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
                .ThenBy(dto => dto.Issue?.Key),
        ];
    }

    private async Task<Issue?> FindByKeyAsync(string key)
    {
        var match = KeyPattern().Match(key);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var number))
        {
            return null;
        }

        var project = await projectAccess.FindRefByKeyAsync(match.Groups[1].Value);
        return project is null ? null : await issues.FirstOrDefaultAsync(item => item.ProjectId == project.Id && item.Number == number);
    }
}
