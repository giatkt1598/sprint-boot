using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Assistant;
using SprintBoot.Issues;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.Host.Mcp;

/// <summary>
/// The MCP tools, offered to the in-app assistant as they are to MCP clients: same names, descriptions and checks. They run
/// in the current request, so as the person chatting.
/// </summary>
[ExposeServices(typeof(IAssistantToolbox))]
public class AssistantToolbox(IServiceProvider services, RichTextSanitizer sanitizer) : IAssistantToolbox, ITransientDependency
{
    private static readonly Type[] ToolTypes = [typeof(IssueTools), typeof(CommentTools), typeof(AgileTools), typeof(WorkTools), typeof(LookupTools)];

    private static readonly Lazy<IReadOnlyList<(AssistantTool Tool, AIFunction Function)>> Catalog = new(Build);

    public IReadOnlyList<AssistantTool> Tools => [.. Catalog.Value.Select(item => item.Tool)];

    public async Task<AssistantToolResult> InvokeAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        var entry = Catalog.Value.FirstOrDefault(item => item.Tool.Name == name);
        if (entry.Function is null)
        {
            return new AssistantToolResult($"There is no tool named {name}.", true);
        }

        var values = arguments.ValueKind == JsonValueKind.Object
            ? arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone())
            : [];
        try
        {
            var result = await entry.Function.InvokeAsync(new AIFunctionArguments(values) { Services = services }, cancellationToken);
            return new AssistantToolResult(result switch
            {
                string text => text,
                JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
                null => string.Empty,
                _ => JsonSerializer.Serialize(result, McpRunner.Json),
            }, false);
        }
        catch (Exception failure) when (Unwrap(failure) is var inner && (inner is McpException || McpRunner.Describe(inner) is not null))
        {
            return new AssistantToolResult(McpRunner.Describe(inner) ?? inner.Message, true);
        }
        catch (Exception failure) when (failure is ArgumentException or JsonException or InvalidOperationException)
        {
            // Arguments the model got wrong: tell it, so it can try again.
            return new AssistantToolResult($"Invalid arguments: {failure.Message}", true);
        }
    }

    public async Task<string> RenderAsync(string markdown) => sanitizer.Clean(await RichTextMarkdown.ToHtmlAsync(markdown)).Html;

    private static Exception Unwrap(Exception failure) =>
        failure is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : failure;

    private static IReadOnlyList<(AssistantTool, AIFunction)> Build() =>
    [
        .. ToolTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => (Type: type, Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>())))
            .Where(item => item.Attribute is not null)
            .Select(item =>
            {
                var name = item.Attribute!.Name ?? item.Method.Name;
                var function = AIFunctionFactory.Create(
                    item.Method,
                    arguments => ActivatorUtilities.CreateInstance(arguments.Services!, item.Type),
                    new AIFunctionFactoryOptions { Name = name });
                return (new AssistantTool(name, function.Description, function.JsonSchema, item.Attribute.ReadOnly), function);
            })
            .OrderBy(item => item.Item1.Name, StringComparer.Ordinal),
    ];
}
