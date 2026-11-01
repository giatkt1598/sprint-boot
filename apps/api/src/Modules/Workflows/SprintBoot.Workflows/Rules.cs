using System.Net.Http.Json;
using System.Text.Json;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace SprintBoot.Workflows;

public record RuleContext(
    TransitionSubject Subject,
    ActorRef Actor,
    IReadOnlyDictionary<Guid, StatusDto> Statuses,
    Guid ToStatusId,
    string? Comment);

/// <summary>Rules are plugins found by key, so a new kind needs no schema change.</summary>
public interface ITransitionRule
{
    string Key { get; }

    RuleKind Kind { get; }
}

public interface ITransitionCondition : ITransitionRule
{
    Task<bool> IsMetAsync(RuleContext context, JsonElement? config);
}

public interface ITransitionValidator : ITransitionRule
{
    /// <summary>Returns problems; empty when the rule passes.</summary>
    Task<IReadOnlyList<RuleProblem>> ValidateAsync(RuleContext context, JsonElement? config);
}

public interface ITransitionPostFunction : ITransitionRule
{
    Task RunAsync(RuleContext context, JsonElement? config);
}

public record RuleProblem(string Code, string? Field = null);

public class RuleRegistry(
    IEnumerable<ITransitionCondition> conditions,
    IEnumerable<ITransitionValidator> validators,
    IEnumerable<ITransitionPostFunction> postFunctions) : ITransientDependency
{
    public ITransitionCondition Condition(string key) =>
        conditions.FirstOrDefault(rule => rule.Key == key) ?? throw Unknown(key);

    public ITransitionValidator Validator(string key) =>
        validators.FirstOrDefault(rule => rule.Key == key) ?? throw Unknown(key);

    public ITransitionPostFunction PostFunction(string key) =>
        postFunctions.FirstOrDefault(rule => rule.Key == key) ?? throw Unknown(key);

    public bool Exists(RuleKind kind, string key) => kind switch
    {
        RuleKind.Condition => conditions.Any(rule => rule.Key == key),
        RuleKind.Validator => validators.Any(rule => rule.Key == key),
        _ => postFunctions.Any(rule => rule.Key == key),
    };

    private static BusinessException Unknown(string key) =>
        new BusinessException(WorkflowErrors.UnknownRule).WithData("rule", key);
}

internal static class RuleConfig
{
    public static string? String(JsonElement? config, string name) =>
        config is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    public static string[] Strings(JsonElement? config, string name) =>
        config is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Array
            ? [.. item.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty)]
            : [];

    public static JsonElement? Element(JsonElement? config, string name) =>
        config is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var item) ? item : null;
}

[ExposeServices(typeof(ITransitionCondition))]
public class RoleCondition(IProjectAccess access) : ITransitionCondition, ITransientDependency
{
    public string Key => "role";

    public RuleKind Kind => RuleKind.Condition;

    public async Task<bool> IsMetAsync(RuleContext context, JsonElement? config)
    {
        var role = await access.GetCurrentRoleAsync(context.Subject.ProjectId);
        return role is not null && RuleConfig.Strings(config, "roles").Any(item => item == role);
    }
}

[ExposeServices(typeof(ITransitionCondition))]
public class UserCondition : ITransitionCondition, ITransientDependency
{
    public string Key => "user";

    public RuleKind Kind => RuleKind.Condition;

    public Task<bool> IsMetAsync(RuleContext context, JsonElement? config)
    {
        var actor = context.Actor.Id;
        var who = RuleConfig.Strings(config, "is");
        return Task.FromResult(
            (who.Contains("assignee") && context.Subject.AssigneeId == actor)
            || (who.Contains("reporter") && context.Subject.ReporterId == actor));
    }
}

[ExposeServices(typeof(ITransitionCondition))]
public class PermissionCondition(IProjectAccess access) : ITransitionCondition, ITransientDependency
{
    public string Key => "permission";

    public RuleKind Kind => RuleKind.Condition;

    public async Task<bool> IsMetAsync(RuleContext context, JsonElement? config) =>
        await access.IsGrantedAsync(context.Subject.ProjectId, RuleConfig.String(config, "permission") ?? string.Empty);
}

[ExposeServices(typeof(ITransitionCondition))]
public class SubtasksDoneCondition(IWorkflowIssueGateway issues) : ITransitionCondition, ITransientDependency
{
    public string Key => "subtasks-done";

    public RuleKind Kind => RuleKind.Condition;

    public async Task<bool> IsMetAsync(RuleContext context, JsonElement? config)
    {
        var statusIds = await issues.GetSubtaskStatusIdsAsync(context.Subject.IssueId);
        return statusIds.All(id => context.Statuses[id].Category == StatusCategory.Done);
    }
}

/// <summary>OR of nested conditions; the top level of a transition is AND.</summary>
[ExposeServices(typeof(ITransitionCondition))]
public class AnyOfCondition(IServiceProvider services) : ITransitionCondition, ITransientDependency
{
    public string Key => "any-of";

    public RuleKind Kind => RuleKind.Condition;

    public async Task<bool> IsMetAsync(RuleContext context, JsonElement? config)
    {
        var registry = (RuleRegistry)services.GetService(typeof(RuleRegistry))!;
        if (RuleConfig.Element(config, "rules") is not { ValueKind: JsonValueKind.Array } rules)
        {
            return false;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            var key = rule.GetProperty("key").GetString()!;
            var nested = rule.TryGetProperty("config", out var value) ? value : (JsonElement?)null;
            if (await registry.Condition(key).IsMetAsync(context, nested))
            {
                return true;
            }
        }

        return false;
    }
}

[ExposeServices(typeof(ITransitionValidator))]
public class RequiredFieldValidator : ITransitionValidator, ITransientDependency
{
    public string Key => "required-field";

    public RuleKind Kind => RuleKind.Validator;

    public Task<IReadOnlyList<RuleProblem>> ValidateAsync(RuleContext context, JsonElement? config)
    {
        IReadOnlyList<RuleProblem> problems =
        [
            .. RuleConfig.Strings(config, "fields")
                .Where(field => !context.Subject.FieldHasValue.GetValueOrDefault(field))
                .Select(field => new RuleProblem(WorkflowErrors.MissingField, field)),
        ];
        return Task.FromResult(problems);
    }
}

[ExposeServices(typeof(ITransitionValidator))]
public class CommentRequiredValidator : ITransitionValidator, ITransientDependency
{
    public string Key => "comment-required";

    public RuleKind Kind => RuleKind.Validator;

    public Task<IReadOnlyList<RuleProblem>> ValidateAsync(RuleContext context, JsonElement? config)
    {
        IReadOnlyList<RuleProblem> problems = string.IsNullOrWhiteSpace(context.Comment)
            ? [new RuleProblem(WorkflowErrors.CommentRequired)]
            : [];
        return Task.FromResult(problems);
    }
}

[ExposeServices(typeof(ITransitionPostFunction))]
public class SetFieldPostFunction(IWorkflowIssueGateway issues) : ITransitionPostFunction, ITransientDependency
{
    public string Key => "set-field";

    public RuleKind Kind => RuleKind.PostFunction;

    public Task RunAsync(RuleContext context, JsonElement? config) => issues.SetFieldAsync(
        context.Subject.IssueId,
        RuleConfig.String(config, "field") ?? string.Empty,
        RuleConfig.Element(config, "value") ?? JsonDocument.Parse("null").RootElement);
}

[ExposeServices(typeof(ITransitionPostFunction))]
public class AssignPostFunction(IWorkflowIssueGateway issues) : ITransitionPostFunction, ITransientDependency
{
    public string Key => "assign";

    public RuleKind Kind => RuleKind.PostFunction;

    public Task RunAsync(RuleContext context, JsonElement? config)
    {
        var target = RuleConfig.String(config, "to") switch
        {
            "reporter" => context.Subject.ReporterId,
            "actor" => context.Actor.Id,
            "user" => Guid.Parse(RuleConfig.String(config, "userId")!),
            _ => throw new BusinessException(WorkflowErrors.InvalidDefinition),
        };
        return issues.SetFieldAsync(context.Subject.IssueId, "assignee", JsonSerializer.SerializeToElement(target));
    }
}

[ExposeServices(typeof(ITransitionPostFunction))]
public class SetResolutionPostFunction(IWorkflowIssueGateway issues) : ITransitionPostFunction, ITransientDependency
{
    public string Key => "set-resolution";

    public RuleKind Kind => RuleKind.PostFunction;

    public Task RunAsync(RuleContext context, JsonElement? config) =>
        issues.SetResolutionAsync(context.Subject.IssueId, RuleConfig.String(config, "value"));
}

[ExposeServices(typeof(ITransitionPostFunction))]
public class AddCommentPostFunction(IWorkflowIssueGateway issues) : ITransitionPostFunction, ITransientDependency
{
    public string Key => "add-comment";

    public RuleKind Kind => RuleKind.PostFunction;

    public Task RunAsync(RuleContext context, JsonElement? config) =>
        issues.AddCommentAsync(context.Subject.IssueId, RuleConfig.String(config, "body") ?? string.Empty);
}

/// <summary>Publishes through the outbox: delivery cannot fail the transition, and a rollback sends nothing.</summary>
[ExposeServices(typeof(ITransitionPostFunction))]
public class NotifyPostFunction(IDistributedEventBus eventBus, IGuidGenerator guids) : ITransitionPostFunction, ITransientDependency
{
    public string Key => "notify";

    public RuleKind Kind => RuleKind.PostFunction;

    public Task RunAsync(RuleContext context, JsonElement? config) => eventBus.PublishAsync(
        new WorkflowNotificationEto
        {
            EventId = guids.Create(),
            IssueId = context.Subject.IssueId,
            ProjectId = context.Subject.ProjectId,
            ActorId = context.Actor.Id,
            Message = RuleConfig.String(config, "message"),
        },
        useOutbox: true);
}

/// <summary>Only records the delivery here; a worker sends it, so failures cannot undo the transition.</summary>
[ExposeServices(typeof(ITransitionPostFunction))]
public class WebhookPostFunction(IRepository<WebhookDelivery, Guid> deliveries, IGuidGenerator guids, IClock clock)
    : ITransitionPostFunction, ITransientDependency
{
    public string Key => "webhook";

    public RuleKind Kind => RuleKind.PostFunction;

    public async Task RunAsync(RuleContext context, JsonElement? config)
    {
        var url = RuleConfig.String(config, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new BusinessException(WorkflowErrors.InvalidDefinition).WithData("url", url);
        }

        var payload = JsonSerializer.Serialize(new
        {
            issueId = context.Subject.IssueId,
            key = context.Subject.Key,
            fromStatus = context.Statuses[context.Subject.StatusId].Name,
            toStatus = context.Statuses[context.ToStatusId].Name,
        });
        await deliveries.InsertAsync(new WebhookDelivery(guids.Create(), url!, payload, clock.Now));
    }
}
