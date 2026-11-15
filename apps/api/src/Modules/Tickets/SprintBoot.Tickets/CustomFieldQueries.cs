using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

/// <summary>
/// Filters and sorts ticket searches by custom fields. Each condition is an EXISTS on ticket_field_values for one
/// field, which the (field, value) indexes answer.
/// </summary>
public class CustomFieldQueries(
    IRepository<CustomField, Guid> fields,
    IRepository<CustomFieldOption, Guid> options,
    IRepository<TicketFieldValue, Guid> values,
    IUserDirectory userDirectory) : ITransientDependency
{
    /// <summary>The fields a search may use: the workspace's, and with a space its own too.</summary>
    public async Task<List<CustomField>> VisibleAsync(Guid? projectId) =>
        await fields.GetListAsync(field => field.ProjectId == null || (projectId != null && field.ProjectId == projectId));

    public async Task<IQueryable<Ticket>> FilterAsync(IQueryable<Ticket> query, Guid? projectId, string[] filters)
    {
        var visible = await VisibleAsync(projectId);
        var rows = await values.GetQueryableAsync();
        foreach (var filter in filters)
        {
            var parts = filter.Split(':', 3);
            var key = CustomFieldRules.StripPrefix(parts[0].Trim());
            var field = visible.FirstOrDefault(item => item.Key == key)
                ?? throw new BusinessException(TicketErrors.UnknownField).WithData("field", CustomFieldConsts.KeyPrefix + key);
            var op = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : string.Empty;
            var text = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            var id = field.Id;
            var mine = rows.Where(value => value.FieldId == id);

            query = (op, field.Type) switch
            {
                ("empty", _) => query.Where(ticket => !mine.Any(value => value.TicketId == ticket.Id)),
                ("any", _) => query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id)),
                ("in", CustomFieldType.User) => In(query, mine, Ids(field, text), value => value.UserId),
                ("in", CustomFieldType.Tags) => Words(query, mine, text),
                ("in", CustomFieldType.Select or CustomFieldType.MultiSelect) => In(query, mine, Ids(field, text), value => value.OptionId),
                ("is", CustomFieldType.Checkbox) => Bool(field, text)
                    ? query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && value.BoolValue == true))
                    : query.Where(ticket => !mine.Any(value => value.TicketId == ticket.Id && value.BoolValue == true)),
                ("gte" or "lte", CustomFieldType.Number) => Range(query, mine, op, Number(field, text)),
                ("gte" or "lte", CustomFieldType.Date) => Range(query, mine, op, Date(field, text)),
                ("has", CustomFieldType.Text or CustomFieldType.Paragraph or CustomFieldType.Url or CustomFieldType.Tags) => Contains(query, mine, text),
                _ => throw CustomFieldRules.Invalid(field, "a filter its kind supports"),
            };
        }

        return query;
    }

    /// <summary>
    /// Orders by a custom field; tickets without a value come last whichever way the list runs. Options sort in the
    /// field's order, people by name.
    /// </summary>
    public async Task<IOrderedQueryable<Ticket>> SortAsync(IQueryable<Ticket> query, CustomField field, bool descending)
    {
        var id = field.Id;
        var mine = (await values.GetQueryableAsync()).Where(value => value.FieldId == id);
        var blankLast = query.OrderBy(ticket => !mine.Any(value => value.TicketId == ticket.Id));

        switch (field.Type)
        {
            case CustomFieldType.Number:
                return blankLast.ThenBy(ticket => mine.Where(value => value.TicketId == ticket.Id).Min(value => value.NumberValue), descending);
            case CustomFieldType.Date:
                return blankLast.ThenBy(ticket => mine.Where(value => value.TicketId == ticket.Id).Min(value => value.DateValue), descending);
            case CustomFieldType.Checkbox:
                return blankLast.ThenBy(ticket => mine.Any(value => value.TicketId == ticket.Id && value.BoolValue == true), descending);
            case CustomFieldType.Select:
            case CustomFieldType.MultiSelect:
                var choices = await options.GetQueryableAsync();
                return blankLast.ThenBy(ticket => mine.Where(value => value.TicketId == ticket.Id)
                    .Select(value => choices.Where(option => option.Id == value.OptionId).Select(option => (int?)option.Order).FirstOrDefault()).Min(), descending);
            case CustomFieldType.User:
                return blankLast.ThenBy(await PersonRanksAsync(mine), descending);
            default:
                return blankLast.ThenBy(ticket => mine.Where(value => value.TicketId == ticket.Id).Min(value => value.TextValue!.ToLower()), descending);
        }
    }

    /// <summary>People's names live in Identity: rank them here and sort by a CASE over the people in the field.</summary>
    private async Task<Expression<Func<Ticket, int>>> PersonRanksAsync(IQueryable<TicketFieldValue> mine)
    {
        var userIds = await mine.Where(value => value.UserId != null).Select(value => value.UserId!.Value).Distinct().ToListAsync();
        var people = await userDirectory.GetByIdsAsync(userIds);
        var ranked = people.Values.OrderBy(user => user.Name, StringComparer.CurrentCultureIgnoreCase).Select((user, index) => (user.Id, index)).ToList();
        Expression<Func<Ticket, Guid?>> person = ticket => mine.Where(value => value.TicketId == ticket.Id).Select(value => value.UserId).FirstOrDefault();
        Expression body = Expression.Constant(ranked.Count);
        foreach (var (userId, index) in ranked.AsEnumerable().Reverse())
        {
            body = Expression.Condition(Expression.Equal(person.Body, Expression.Constant(userId, typeof(Guid?))), Expression.Constant(index), body);
        }

        return Expression.Lambda<Func<Ticket, int>>(body, person.Parameters);
    }

    private static IQueryable<Ticket> In(IQueryable<Ticket> query, IQueryable<TicketFieldValue> mine, List<Guid> ids, Expression<Func<TicketFieldValue, Guid?>> column)
    {
        var parameter = column.Parameters[0];
        var ticketParameter = Expression.Parameter(typeof(Ticket), "ticket");
        var contains = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid?)],
            Expression.Constant(ids.Select(id => (Guid?)id).ToList()), column.Body);
        var sameTicket = Expression.Equal(Expression.Property(parameter, nameof(TicketFieldValue.TicketId)), Expression.Property(ticketParameter, nameof(Ticket.Id)));
        var match = Expression.Lambda<Func<TicketFieldValue, bool>>(Expression.AndAlso(sameTicket, contains), parameter);
        var any = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(TicketFieldValue)], mine.Expression, Expression.Quote(match));
        return query.Where(Expression.Lambda<Func<Ticket, bool>>(any, ticketParameter));
    }

    private static IQueryable<Ticket> Range(IQueryable<Ticket> query, IQueryable<TicketFieldValue> mine, string op, decimal bound) =>
        op == "gte"
            ? query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && value.NumberValue >= bound))
            : query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && value.NumberValue <= bound));

    private static IQueryable<Ticket> Range(IQueryable<Ticket> query, IQueryable<TicketFieldValue> mine, string op, DateOnly bound) =>
        op == "gte"
            ? query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && value.DateValue >= bound))
            : query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && value.DateValue <= bound));

    /// <summary>Tickets with any of the words, compared without case.</summary>
    private static IQueryable<Ticket> Words(IQueryable<Ticket> query, IQueryable<TicketFieldValue> mine, string text)
    {
        var words = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToLower()).ToList();
        return query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && words.Contains(value.TextValue!.ToLower())));
    }

    private static IQueryable<Ticket> Contains(IQueryable<Ticket> query, IQueryable<TicketFieldValue> mine, string text)
    {
        var pattern = $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        return query.Where(ticket => mine.Any(value => value.TicketId == ticket.Id && EF.Functions.ILike(value.TextValue!, pattern)));
    }

    private static List<Guid> Ids(CustomField field, string text)
    {
        var ids = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => Guid.TryParse(item, out var id) ? id : throw CustomFieldRules.Invalid(field, "ids")).ToList();
        return ids.Count > 0 ? ids : throw CustomFieldRules.Invalid(field, "ids");
    }

    private static bool Bool(CustomField field, string text) => bool.TryParse(text, out var value) ? value : throw CustomFieldRules.Invalid(field, "true or false");

    private static decimal Number(CustomField field, string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : throw CustomFieldRules.Invalid(field, "number");

    private static DateOnly Date(CustomField field, string text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : throw CustomFieldRules.Invalid(field, "date");
}
