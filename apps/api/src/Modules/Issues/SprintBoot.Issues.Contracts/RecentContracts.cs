using Volo.Abp.Application.Services;

namespace SprintBoot.Issues;

/// <summary>The issues and epics a person opened lately, for getting back to them quickly.</summary>
public interface IRecentIssueAppService : IApplicationService
{
    /// <summary>Notes that the caller opened the issue; opening it again only moves it to the top. Needs the right to view it.</summary>
    Task VisitAsync(Guid issueId);

    /// <summary>The caller's latest ones, newest first, leaving out what they can no longer open.</summary>
    Task<List<IssueSummaryDto>> GetListAsync();
}
