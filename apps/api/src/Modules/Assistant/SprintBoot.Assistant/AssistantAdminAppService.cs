using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SprintBoot.Identity;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace SprintBoot.Assistant;

/// <summary>Connections to AI providers and the switch for the whole feature. Keys are stored encrypted and never returned.</summary>
[Authorize(SprintBootPermissions.ManageAi)]
public class AssistantAdminAppService(
    IRepository<AiConnection, Guid> connections,
    ISettingManager settingManager,
    ISettingProvider settings,
    IStringEncryptionService encryption,
    ChatProviders providers)
    : ApplicationService, IAssistantAdminAppService
{
    private static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    public async Task<AssistantSettingsDto> GetAsync() => new()
    {
        Enabled = await settings.IsTrueAsync(AssistantSettings.Enabled),
        Connections = [.. (await connections.GetListAsync()).OrderByDescending(item => item.IsDefault).ThenBy(item => item.Name).Select(ToDto)],
    };

    public async Task<AssistantSettingsDto> UpdateAsync(UpdateAssistantSettingsInput input)
    {
        await settingManager.SetGlobalAsync(AssistantSettings.Enabled, input.Enabled.ToString());
        return await GetAsync();
    }

    public async Task<AiConnectionDto> CreateConnectionAsync(SaveAiConnectionInput input)
    {
        var connection = new AiConnection(GuidGenerator.Create());
        // The first connection becomes the default, so the feature works as soon as it is switched on.
        await ApplyAsync(connection, input, isFirst: !await connections.AnyAsync());
        await connections.InsertAsync(connection, autoSave: true);
        return ToDto(connection);
    }

    public async Task<AiConnectionDto> UpdateConnectionAsync(Guid id, SaveAiConnectionInput input)
    {
        var connection = await connections.GetAsync(id);
        await ApplyAsync(connection, input, isFirst: false);
        await connections.UpdateAsync(connection, autoSave: true);
        return ToDto(connection);
    }

    public async Task DeleteConnectionAsync(Guid id)
    {
        var connection = await connections.GetAsync(id);
        await connections.DeleteAsync(connection, autoSave: true);
        if (connection.IsDefault && (await connections.GetListAsync()).OrderBy(item => item.Name).FirstOrDefault() is { } next)
        {
            next.IsDefault = true;
            await connections.UpdateAsync(next, autoSave: true);
        }
    }

    public async Task<List<AiModelDto>> ListModelsAsync(ListAiModelsInput input)
    {
        var connection = await FromFormAsync(input, string.Empty, null);
        try
        {
            var models = await providers.For(input.Provider).ListModelsAsync(connection, CancellationToken.None);
            return [.. models.DistinctBy(model => model.Id).OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception failure) when (failure is not BusinessException and not OperationCanceledException)
        {
            Logger.LogWarning(failure, "Listing models of {Provider} failed", input.Provider);
            throw new BusinessException(AssistantErrors.ModelsUnavailable).WithData("reason", failure.Message);
        }
    }

    public async Task<TestAiConnectionResultDto> TestConnectionAsync(Guid id) =>
        await ProbeAsync(ToChat(await connections.GetAsync(id), encryption));

    public async Task<TestAiConnectionResultDto> TestSettingsAsync(TestAiSettingsInput input)
    {
        var effort = string.IsNullOrWhiteSpace(input.Effort) ? null : input.Effort.Trim().ToLowerInvariant();
        if (effort is not null && (input.Provider != AiProvider.Anthropic || !Efforts.Contains(effort)))
        {
            throw new BusinessException(AssistantErrors.InvalidEffort);
        }

        return await ProbeAsync(await FromFormAsync(input, input.Model.Trim(), effort));
    }

    /// <summary>One short prompt; the answer, or what the provider said went wrong.</summary>
    private async Task<TestAiConnectionResultDto> ProbeAsync(ChatConnection connection)
    {
        var request = new ChatRequest("You check that a connection works. Answer in a few words.", [new UserTurn("Say hello.")], []);
        var reply = new StringBuilder();
        try
        {
            await foreach (var item in providers.For(connection.Provider).StreamAsync(connection, request, CancellationToken.None))
            {
                if (item is ChatTextDelta delta)
                {
                    reply.Append(delta.Text);
                }
            }

            return new TestAiConnectionResultDto { Succeeded = true, Message = reply.Length > 0 ? reply.ToString().Trim() : "OK" };
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Logger.LogWarning(failure, "Testing AI connection to {Provider} failed", connection.Provider);
            return new TestAiConnectionResultDto { Succeeded = false, Message = failure.Message };
        }
    }

    /// <summary>Settings typed in the form. Editing leaves the key field blank; the stored key is used then.</summary>
    private async Task<ChatConnection> FromFormAsync(ListAiModelsInput input, string model, string? effort)
    {
        var baseUrl = string.IsNullOrWhiteSpace(input.BaseUrl) ? null : input.BaseUrl.Trim();
        if (input.Provider == AiProvider.OpenAiCompatible && (baseUrl is null || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _)))
        {
            throw new BusinessException(AssistantErrors.BaseUrlRequired);
        }

        var apiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? null : input.ApiKey.Trim();
        if (apiKey is null && input.ConnectionId is { } id && (await connections.GetAsync(id)).EncryptedApiKey is { } stored)
        {
            apiKey = encryption.Decrypt(stored);
        }

        return new ChatConnection(input.Provider, baseUrl, apiKey, model, null, effort);
    }

    internal static ChatConnection ToChat(AiConnection connection, IStringEncryptionService encryption) => new(
        connection.Provider,
        connection.BaseUrl,
        connection.EncryptedApiKey is null ? null : encryption.Decrypt(connection.EncryptedApiKey),
        connection.Model,
        connection.MaxOutputTokens,
        connection.Effort);

    private async Task ApplyAsync(AiConnection connection, SaveAiConnectionInput input, bool isFirst)
    {
        var baseUrl = string.IsNullOrWhiteSpace(input.BaseUrl) ? null : input.BaseUrl.Trim();
        if (input.Provider == AiProvider.OpenAiCompatible && (baseUrl is null || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _)))
        {
            throw new BusinessException(AssistantErrors.BaseUrlRequired);
        }

        var effort = string.IsNullOrWhiteSpace(input.Effort) ? null : input.Effort.Trim().ToLowerInvariant();
        if (effort is not null && (input.Provider != AiProvider.Anthropic || !Efforts.Contains(effort)))
        {
            throw new BusinessException(AssistantErrors.InvalidEffort);
        }

        connection.Name = input.Name.Trim();
        connection.Provider = input.Provider;
        connection.BaseUrl = baseUrl;
        connection.Model = input.Model.Trim();
        connection.MaxOutputTokens = input.MaxOutputTokens;
        connection.MaxRequestKb = input.MaxRequestKb;
        connection.Effort = effort;
        if (!string.IsNullOrEmpty(input.ApiKey))
        {
            connection.EncryptedApiKey = encryption.Encrypt(input.ApiKey.Trim());
        }

        if (input.IsDefault || isFirst)
        {
            foreach (var other in await connections.GetListAsync(item => item.IsDefault && item.Id != connection.Id))
            {
                other.IsDefault = false;
                await connections.UpdateAsync(other);
            }

            connection.IsDefault = true;
        }
    }

    private static AiConnectionDto ToDto(AiConnection connection) => new()
    {
        Id = connection.Id,
        Name = connection.Name,
        Provider = connection.Provider,
        BaseUrl = connection.BaseUrl,
        Model = connection.Model,
        MaxOutputTokens = connection.MaxOutputTokens,
        MaxRequestKb = connection.MaxRequestKb,
        Effort = connection.Effort,
        HasApiKey = connection.EncryptedApiKey is not null,
        IsDefault = connection.IsDefault,
    };
}
