using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Issues;

/// <summary>Orders issue search results by one of the columns the issue list shows.</summary>
/// <remarks>
/// Statuses, people and space keys live in other modules, so their order is read through their contracts
/// and turned into a rank the database can sort by.
/// </remarks>
public class IssueSorting(
    IRepository<Priority, Guid> priorities,
    IStatusLookup statusLookup,
    IUserDirectory userDirectory,
    IProjectAccess projectAccess) : Volo.Abp.DependencyInjection.ITransientDependency
{
    public static readonly string[] Fields = ["key", "title", "status", "priority", "assignee", "reporter", "created", "updated"];

    public async Task<IOrderedQueryable<Issue>> ApplyAsync(IQueryable<Issue> query, string? sorting)
    {
        var (field, descending) = Parse(sorting);
        IOrderedQueryable<Issue> ordered = field switch
        {
            null => query.OrderByDescending(issue => issue.LastModificationTime ?? issue.CreationTime),
            "updated" => Order(query, issue => issue.LastModificationTime ?? issue.CreationTime, descending),
            "created" => Order(query, issue => issue.CreationTime, descending),
            "title" => Order(query, issue => issue.Title.ToLower(), descending),
            "key" => Order(query, ByRank(issue => issue.ProjectId, await ProjectRanksAsync(query)), descending)
                .ThenBy(issue => issue.Number, descending),
            "priority" => Order(query, ByRank(issue => issue.PriorityId, await PriorityRanksAsync()), descending),
            "status" => Order(query, ByRank(issue => issue.StatusId, await StatusRanksAsync()), descending),
            // Issues nobody is assigned to, or nobody reported, come last whichever way the list runs.
            "assignee" => query.OrderBy(issue => issue.AssigneeId == null)
                .ThenBy(ByRank(issue => issue.AssigneeId, await PersonRanksAsync(query, issue => issue.AssigneeId)), descending),
            "reporter" => query.OrderBy(issue => issue.ReporterId == null)
                .ThenBy(ByRank(issue => issue.ReporterId, await PersonRanksAsync(query, issue => issue.ReporterId)), descending),
            _ => throw new BusinessException(IssueErrors.InvalidSorting).WithData("field", field),
        };
        return ordered.ThenBy(issue => issue.Id);
    }

    /// <summary>Accepts "field" or "field asc|desc"; anything else is refused rather than guessed.</summary>
    internal static (string? Field, bool Descending) Parse(string? sorting)
    {
        if (string.IsNullOrWhiteSpace(sorting))
        {
            return (null, false);
        }

        var parts = sorting.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts[0].ToLowerInvariant();
        var direction = parts.Length > 1 ? parts[1].ToLowerInvariant() : "asc";
        if (parts.Length > 2 || !Fields.Contains(field) || direction is not ("asc" or "desc"))
        {
            throw new BusinessException(IssueErrors.InvalidSorting).WithData("field", sorting);
        }

        return (field, direction == "desc");
    }

    private async Task<Dictionary<Guid, int>> PriorityRanksAsync()
    {
        var list = await priorities.GetListAsync();
        return Ranks(list.OrderBy(priority => priority.Order).Select(priority => priority.Id));
    }

    /// <summary>Statuses follow the workflow: to do, in progress, done, then by name within each.</summary>
    private async Task<Dictionary<Guid, int>> StatusRanksAsync()
    {
        var list = await statusLookup.GetListAsync();
        return Ranks(list.OrderBy(status => status.Category).ThenBy(status => status.Name, StringComparer.CurrentCultureIgnoreCase).Select(status => status.Id));
    }

    private async Task<Dictionary<Guid, int>> ProjectRanksAsync(IQueryable<Issue> query)
    {
        var ids = await query.Select(issue => issue.ProjectId).Distinct().ToListAsync();
        var refs = await projectAccess.GetRefsAsync(ids);
        return Ranks(refs.Values.OrderBy(project => project.Key, StringComparer.Ordinal).Select(project => project.Id));
    }

    private async Task<Dictionary<Guid, int>> PersonRanksAsync(IQueryable<Issue> query, Expression<Func<Issue, Guid?>> person)
    {
        var ids = await query.Select(person).Where(id => id != null).Distinct().ToListAsync();
        var people = await userDirectory.GetByIdsAsync(ids.Select(id => id!.Value));
        return Ranks(people.Values.OrderBy(user => user.Name, StringComparer.CurrentCultureIgnoreCase).Select(user => user.Id));
    }

    private static Dictionary<Guid, int> Ranks(IEnumerable<Guid> ordered) =>
        ordered.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);

    /// <summary>A CASE over the known ids; an id without a rank sorts after the ranked ones.</summary>
    private static Expression<Func<Issue, int>> ByRank(Expression<Func<Issue, Guid>> selector, Dictionary<Guid, int> ranks) =>
        ByRank(Expression.Lambda<Func<Issue, Guid?>>(Expression.Convert(selector.Body, typeof(Guid?)), selector.Parameters), ranks);

    private static Expression<Func<Issue, int>> ByRank(Expression<Func<Issue, Guid?>> selector, Dictionary<Guid, int> ranks)
    {
        Expression body = Expression.Constant(ranks.Count);
        foreach (var (id, rank) in ranks.OrderByDescending(item => item.Value))
        {
            body = Expression.Condition(
                Expression.Equal(selector.Body, Expression.Constant(id, typeof(Guid?))),
                Expression.Constant(rank),
                body);
        }

        return Expression.Lambda<Func<Issue, int>>(body, selector.Parameters);
    }

    private static IOrderedQueryable<Issue> Order<TKey>(IQueryable<Issue> query, Expression<Func<Issue, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);
}

internal static class OrderedQueryableExtensions
{
    public static IOrderedQueryable<T> ThenBy<T, TKey>(this IOrderedQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.ThenByDescending(key) : query.ThenBy(key);
}
