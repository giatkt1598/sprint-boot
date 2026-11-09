using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

/// <summary>Orders ticket search results by one of the columns the ticket list shows.</summary>
/// <remarks>
/// Statuses, people and space keys live in other modules, so their order is read through their contracts
/// and turned into a rank the database can sort by.
/// </remarks>
public class TicketSorting(
    IRepository<Priority, Guid> priorities,
    IStatusLookup statusLookup,
    IUserDirectory userDirectory,
    IProjectAccess projectAccess,
    ITicketSprintNames sprintNames) : Volo.Abp.DependencyInjection.ITransientDependency
{
    public static readonly string[] Fields = ["key", "title", "status", "priority", "assignee", "reporter", "sprint", "created", "updated"];

    public async Task<IOrderedQueryable<Ticket>> ApplyAsync(IQueryable<Ticket> query, string? sorting)
    {
        var (field, descending) = Parse(sorting);
        IOrderedQueryable<Ticket> ordered = field switch
        {
            null => query.OrderByDescending(ticket => ticket.LastModificationTime ?? ticket.CreationTime),
            "updated" => Order(query, ticket => ticket.LastModificationTime ?? ticket.CreationTime, descending),
            "created" => Order(query, ticket => ticket.CreationTime, descending),
            "title" => Order(query, ticket => ticket.Title.ToLower(), descending),
            "key" => Order(query, ByRank(ticket => ticket.ProjectId, await ProjectRanksAsync(query)), descending)
                .ThenBy(ticket => ticket.Number, descending),
            "priority" => Order(query, ByRank(ticket => ticket.PriorityId, await PriorityRanksAsync()), descending),
            "status" => Order(query, ByRank(ticket => ticket.StatusId, await StatusRanksAsync()), descending),
            // Tickets nobody is assigned to, or nobody reported, come last whichever way the list runs.
            "assignee" => query.OrderBy(ticket => ticket.AssigneeId == null)
                .ThenBy(ByRank(ticket => ticket.AssigneeId, await PersonRanksAsync(query, ticket => ticket.AssigneeId)), descending),
            "reporter" => query.OrderBy(ticket => ticket.ReporterId == null)
                .ThenBy(ByRank(ticket => ticket.ReporterId, await PersonRanksAsync(query, ticket => ticket.ReporterId)), descending),
            // Sprints in time order; tickets in no sprint come last whichever way the list runs.
            "sprint" => await BySprintAsync(query, descending),
            _ => throw new BusinessException(TicketErrors.InvalidSorting).WithData("field", field),
        };
        return ordered.ThenBy(ticket => ticket.Id);
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
            throw new BusinessException(TicketErrors.InvalidSorting).WithData("field", sorting);
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

    private async Task<Dictionary<Guid, int>> ProjectRanksAsync(IQueryable<Ticket> query)
    {
        var ids = await query.Select(ticket => ticket.ProjectId).Distinct().ToListAsync();
        var refs = await projectAccess.GetRefsAsync(ids);
        return Ranks(refs.Values.OrderBy(project => project.Key, StringComparer.Ordinal).Select(project => project.Id));
    }

    private async Task<Dictionary<Guid, int>> PersonRanksAsync(IQueryable<Ticket> query, Expression<Func<Ticket, Guid?>> person)
    {
        var ids = await query.Select(person).Where(id => id != null).Distinct().ToListAsync();
        var people = await userDirectory.GetByIdsAsync(ids.Select(id => id!.Value));
        return Ranks(people.Values.OrderBy(user => user.Name, StringComparer.CurrentCultureIgnoreCase).Select(user => user.Id));
    }

    /// <summary>
    /// Sprints live in the Agile module, so each ticket's sprint comes back as a rank through its contract. The tickets
    /// of one sprint share one IN list, so the CASE has a branch per sprint rather than per ticket.
    /// </summary>
    private async Task<IOrderedQueryable<Ticket>> BySprintAsync(IQueryable<Ticket> query, bool descending)
    {
        var ids = await query.Select(ticket => ticket.Id).ToListAsync();
        var ranks = await sprintNames.GetRanksAsync(ids);
        var parameter = Expression.Parameter(typeof(Ticket), "ticket");
        var id = Expression.Property(parameter, nameof(Ticket.Id));
        var contains = typeof(Enumerable).GetMethods()
            .First(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(Guid));
        Expression body = Expression.Constant(int.MaxValue);
        foreach (var group in ranks.GroupBy(item => item.Value).OrderByDescending(group => group.Key))
        {
            var members = group.Select(item => item.Key).ToList();
            body = Expression.Condition(Expression.Call(contains, Expression.Constant(members), id), Expression.Constant(group.Key), body);
        }

        var ranked = ranks.Keys.ToList();
        return query.OrderBy(ticket => !ranked.Contains(ticket.Id))
            .ThenBy(Expression.Lambda<Func<Ticket, int>>(body, parameter), descending);
    }

    private static Dictionary<Guid, int> Ranks(IEnumerable<Guid> ordered) =>
        ordered.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);

    /// <summary>A CASE over the known ids; an id without a rank sorts after the ranked ones.</summary>
    private static Expression<Func<Ticket, int>> ByRank(Expression<Func<Ticket, Guid>> selector, Dictionary<Guid, int> ranks) =>
        ByRank(Expression.Lambda<Func<Ticket, Guid?>>(Expression.Convert(selector.Body, typeof(Guid?)), selector.Parameters), ranks);

    private static Expression<Func<Ticket, int>> ByRank(Expression<Func<Ticket, Guid?>> selector, Dictionary<Guid, int> ranks)
    {
        Expression body = Expression.Constant(ranks.Count);
        foreach (var (id, rank) in ranks.OrderByDescending(item => item.Value))
        {
            body = Expression.Condition(
                Expression.Equal(selector.Body, Expression.Constant(id, typeof(Guid?))),
                Expression.Constant(rank),
                body);
        }

        return Expression.Lambda<Func<Ticket, int>>(body, selector.Parameters);
    }

    private static IOrderedQueryable<Ticket> Order<TKey>(IQueryable<Ticket> query, Expression<Func<Ticket, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);
}

internal static class OrderedQueryableExtensions
{
    public static IOrderedQueryable<T> ThenBy<T, TKey>(this IOrderedQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.ThenByDescending(key) : query.ThenBy(key);
}
