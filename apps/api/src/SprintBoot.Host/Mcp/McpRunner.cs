using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using SprintBoot.Identity;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Uow;
using Volo.Abp.Validation;

namespace SprintBoot.Host.Mcp;

/// <summary>Runs each tool call in its own unit of work and turns refusals into MCP errors the client can read.</summary>
public class McpRunner(IHttpContextAccessor http, IUnitOfWorkManager unitOfWork) : ITransientDependency
{
    /// <summary>Beyond this the client would choke; the caller should narrow the request.</summary>
    public const int MaxResultLength = 200_000;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<string> ReadAsync<T>(Func<Task<T>> action) => RunAsync(action);

    public Task<string> WriteAsync<T>(Func<Task<T>> action)
    {
        EnsureWritable();
        return RunAsync(action);
    }

    public void EnsureWritable()
    {
        if (http.HttpContext is { } context && PersonalTokenGate.IsReadOnly(context.User))
        {
            throw new McpException("This token is read-only.");
        }
    }

    /// <summary>For batches: one item's failure is reported and rolled back without stopping the rest.</summary>
    public async Task<(T? Value, string? Error)> TryAsync<T>(Func<Task<T>> action)
    {
        try
        {
            using var scope = unitOfWork.Begin(requiresNew: true, isTransactional: true);
            var value = await action();
            await scope.CompleteAsync();
            return (value, null);
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            return (default, message);
        }
    }

    private async Task<string> RunAsync<T>(Func<Task<T>> action)
    {
        T result;
        try
        {
            // A refusal must not leave half-applied changes for the request's own unit of work to save.
            using var scope = unitOfWork.Begin(requiresNew: true, isTransactional: true);
            result = await action();
            await scope.CompleteAsync();
        }
        catch (Exception exception) when (exception is not McpException && Describe(exception) is { } message)
        {
            throw new McpException(message);
        }

        var json = JsonSerializer.Serialize(result, Json);
        return json.Length <= MaxResultLength
            ? json
            : throw new McpException("The result is too large. Ask for fewer items with limit and offset, or narrow the filters.");
    }

    /// <summary>The client only sees exceptions raised as MCP errors, so domain refusals become those.</summary>
    public static string? Describe(Exception exception) => exception switch
    {
        McpException mcp => mcp.Message,
        AbpValidationException validation => "Invalid input: " + string.Join("; ", validation.ValidationErrors.Select(error => error.ErrorMessage)),
        BusinessException business => (business.Code ?? "The request was refused.") + Details(business.Data),
        EntityNotFoundException => "Not found.",
        AbpAuthorizationException => "You do not have permission for this.",
        AbpDbConcurrencyException => "The item changed since it was read. Read it again and retry.",
        _ => null,
    };

    private static string Details(IDictionary data)
    {
        var pairs = data.Cast<DictionaryEntry>().Select(entry => $"{entry.Key}: {entry.Value}").ToList();
        return pairs.Count == 0 ? string.Empty : $" ({string.Join(", ", pairs)})";
    }
}
