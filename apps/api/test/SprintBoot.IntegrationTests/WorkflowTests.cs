using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>One test per acceptance criterion of the Workflow page.</summary>
[Collection(ApiCollection.Name)]
public class WorkflowTests(ApiFixture fixture)
{
    private static readonly Guid ToDo = WorkflowsDataSeedContributor.ToDoId;
    private static readonly Guid InProgress = WorkflowsDataSeedContributor.InProgressId;
    private static readonly Guid Done = WorkflowsDataSeedContributor.DoneId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    private static async Task<ProjectDto> CreateProjectAsync(AuthClient client)
    {
        var key = $"W{Random.Shared.Next(100000, 999999)}";
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Flow", key, type = 0 }, Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<Guid> TaskTypeAsync(AuthClient client, Guid projectId)
    {
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{projectId}", Token);
        return types!.Single(type => type.Name == "Task").Id;
    }

    private static async Task<IssueDto> CreateIssueAsync(AuthClient client, Guid projectId, Guid typeId, Guid? parentId = null)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId, typeId, title = "Work", parentId }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<IssueDto>(Token))!;
    }

    private static Task<IssueDto?> ReloadAsync(AuthClient client, string key) =>
        client.Http.GetFromJsonAsync<IssueDto>($"/api/v1/issue/by-key?key={key}", Token);

    private static Task<List<AvailableTransitionDto>?> AvailableAsync(AuthClient client, Guid issueId) =>
        client.Http.GetFromJsonAsync<List<AvailableTransitionDto>>($"/api/v1/transition/available/{issueId}", Token);

    private static Task<HttpResponseMessage> ExecuteAsync(AuthClient client, IssueDto issue, Guid transitionId, string? comment = null) =>
        client.Http.PostAsJsonAsync(
            $"/api/v1/transition/execute/{issue.Id}",
            new { transitionId, concurrencyStamp = issue.ConcurrencyStamp, comment },
            Token);

    /// <summary>Creates and publishes a workflow, then maps it to the project's Task type.</summary>
    private async Task<(AuthClient Admin, ProjectDto Project, Guid TypeId, Guid WorkflowId)> ProjectWithWorkflowAsync(object transitions, Guid[]? statuses = null)
    {
        var admin = await AdminAsync();
        var project = await CreateProjectAsync(admin);
        var typeId = await TaskTypeAsync(admin, project.Id);
        var created = await admin.Http.PostAsJsonAsync("/api/v1/workflow", new
        {
            name = $"Flow {Guid.NewGuid():N}",
            definition = new { initialStatusId = ToDo, statusIds = statuses ?? [ToDo, Done], transitions },
        }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var workflow = (await created.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        var published = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflow.Id}/publish", new { }, Token);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
        var mapped = await admin.Http.PostAsJsonAsync(
            "/api/v1/project-workflow/set", new { projectId = project.Id, issueTypeId = typeId, workflowId = workflow.Id }, Token);
        mapped.StatusCode.ShouldBe(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync(Token));
        return (admin, project, typeId, workflow.Id);
    }

    private static readonly Guid[] AllThree = [ToDo, InProgress, Done];

    private static object Move(string name, Guid? from, Guid to, params object[] rules) =>
        new { name, fromStatusId = from, toStatusId = to, rules };

    private static object Rule(int kind, string key, object? config = null) => new { kind, key, config };

    private static readonly object[] Basic =
    [
        Move("Start", ToDo, InProgress),
        Move("Finish", InProgress, Done),
    ];

    [Fact]
    public async Task New_project_starts_on_the_default_workflow()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issue = await CreateIssueAsync(client, project.Id, await TaskTypeAsync(client, project.Id));

        issue.Status.Name.ShouldBe("To Do");
        var available = await AvailableAsync(client, issue.Id);
        available!.Select(item => item.ToStatus.Name).ShouldBe(["In Progress", "Done"], ignoreOrder: true);
    }

    [Fact]
    public async Task Transition_changes_status_writes_history_and_event_path()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issue = await CreateIssueAsync(client, project.Id, await TaskTypeAsync(client, project.Id));
        var move = (await AvailableAsync(client, issue.Id))!.Single(item => item.ToStatus.Name == "Done");

        (await ExecuteAsync(client, issue, move.Id)).EnsureSuccessStatusCode();

        (await ReloadAsync(client, issue.Key))!.Status.Name.ShouldBe("Done");
        var activity = await client.Http.GetFromJsonAsync<List<IssueActivityDto>>($"/api/v1/issue/{issue.Id}/activity", Token);
        var row = activity!.Single(item => item.Field == "status");
        row.OldValue.ShouldBe("To Do");
        row.NewValue.ShouldBe("Done");
    }

    [Fact]
    public async Task Condition_hides_transition_and_blocks_a_direct_call()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move("Assignee only", null, Done, Rule(0, "user", new { @is = new[] { "assignee" } })),
            Move("Start", ToDo, InProgress),
        }, AllThree);
        var issue = await CreateIssueAsync(admin, project.Id, typeId);

        var available = await AvailableAsync(admin, issue.Id);
        available!.Select(item => item.Name).ShouldBe(["Start"]);

        var hidden = await TransitionIdByNameAsync(project.Id, typeId, "Assignee only");
        var response = await ExecuteAsync(admin, issue, hidden);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReloadAsync(admin, issue.Key))!.Status.Name.ShouldBe("To Do");
    }

    private Task<Guid> TransitionIdByNameAsync(Guid projectId, Guid typeId, string name) =>
        fixture.FindTransitionIdAsync(projectId, typeId, name);

    [Fact]
    public async Task Any_status_shows_everywhere_except_its_target()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move("Start", ToDo, InProgress),
            Move("Close", null, Done),
        }, AllThree);
        var issue = await CreateIssueAsync(admin, project.Id, typeId);

        (await AvailableAsync(admin, issue.Id))!.Select(item => item.Name).ShouldBe(["Start", "Close"], ignoreOrder: true);

        var close = (await AvailableAsync(admin, issue.Id))!.Single(item => item.Name == "Close");
        (await ExecuteAsync(admin, issue, close.Id)).EnsureSuccessStatusCode();
        (await AvailableAsync(admin, issue.Id))!.ShouldNotContain(item => item.Name == "Close");
    }

    [Fact]
    public async Task Failed_validator_keeps_status_and_returns_the_reason()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move("Finish", null, Done, Rule(1, "required-field", new { fields = new[] { "assignee" } }), Rule(1, "comment-required")),
        });
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        var move = (await AvailableAsync(admin, issue.Id))!.Single();

        move.RequiresComment.ShouldBeTrue();
        move.MissingFields.ShouldBe(["assignee"]);

        var response = await ExecuteAsync(admin, issue, move.Id, "done");

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.ValidationFailed);
        (await ReloadAsync(admin, issue.Key))!.Status.Name.ShouldBe("To Do");
    }

    [Fact]
    public async Task Failing_internal_post_function_rolls_everything_back()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move("Finish", null, Done, Rule(2, "add-comment", new { body = "<p>Auto</p>" }), Rule(2, "set-field", new { field = "no-such-field", value = "x" })),
        });
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        var move = (await AvailableAsync(admin, issue.Id))!.Single();

        var response = await ExecuteAsync(admin, issue, move.Id);

        response.IsSuccessStatusCode.ShouldBeFalse();
        (await ReloadAsync(admin, issue.Key))!.Status.Name.ShouldBe("To Do");
        var comments = await admin.Http.GetFromJsonAsync<List<CommentDto>>($"/api/v1/comment?issueId={issue.Id}", Token);
        comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Post_functions_assign_set_resolution_and_comment_in_the_transition()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move(
                "Finish",
                null,
                Done,
                Rule(2, "assign", new { to = "actor" }),
                Rule(2, "set-resolution", new { value = "Fixed" }),
                Rule(2, "add-comment", new { body = "<p>Closed</p>" })),
        });
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        var move = (await AvailableAsync(admin, issue.Id))!.Single();

        (await ExecuteAsync(admin, issue, move.Id)).EnsureSuccessStatusCode();

        (await ReloadAsync(admin, issue.Key))!.Assignee.ShouldNotBeNull();
        (await admin.Http.GetFromJsonAsync<List<CommentDto>>($"/api/v1/comment?issueId={issue.Id}", Token))!.Single().Body.ShouldBe("<p>Closed</p>");
    }

    [Fact]
    public async Task Subtask_condition_waits_for_children_to_finish()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move("Finish", null, Done, Rule(0, "subtasks-done")),
        });
        var parent = await CreateIssueAsync(admin, project.Id, typeId);
        var types = await admin.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var child = await CreateIssueAsync(admin, project.Id, types!.Single(type => type.Name == "Subtask").Id, parent.Id);

        (await AvailableAsync(admin, parent.Id)).ShouldBeEmpty();

        var close = (await AvailableAsync(admin, child.Id))!.Single(item => item.ToStatus.Name == "Done");
        (await ExecuteAsync(admin, child, close.Id)).EnsureSuccessStatusCode();

        (await AvailableAsync(admin, parent.Id))!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Failing_webhook_keeps_the_transition_and_is_retried()
    {
        using var receiver = new HttpListener();
        var port = ApiFixture.GetFreePort();
        receiver.Prefixes.Add($"http://localhost:{port}/");
        receiver.Start();
        var calls = 0;
        _ = Task.Run(async () =>
        {
            while (receiver.IsListening)
            {
                var context = await receiver.GetContextAsync();
                context.Response.StatusCode = Interlocked.Increment(ref calls) == 1 ? 500 : 200;
                context.Response.Close();
            }
        }, Token);

        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            Move("Finish", null, Done, Rule(2, "webhook", new { url = $"http://localhost:{port}/hook" })),
        });
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        var move = (await AvailableAsync(admin, issue.Id))!.Single();

        (await ExecuteAsync(admin, issue, move.Id)).EnsureSuccessStatusCode();
        (await ReloadAsync(admin, issue.Key))!.Status.Name.ShouldBe("Done");

        for (var wait = 0; wait < 100 && Volatile.Read(ref calls) < 2; wait++)
        {
            await Task.Delay(200, Token);
        }

        Volatile.Read(ref calls).ShouldBe(2);
        receiver.Stop();
    }

    [Fact]
    public async Task Unreachable_status_blocks_publish()
    {
        var admin = await AdminAsync();
        var created = await admin.Http.PostAsJsonAsync("/api/v1/workflow", new
        {
            name = $"Broken {Guid.NewGuid():N}",
            definition = new { initialStatusId = ToDo, statusIds = new[] { ToDo, InProgress, Done }, transitions = new[] { Move("Finish", ToDo, Done) } },
        }, Token);
        var workflow = (await created.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;

        var response = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflow.Id}/publish", new { }, Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.UnreachableStatus);
    }

    [Fact]
    public async Task Publish_dropping_a_used_status_requires_a_mapping_and_moves_the_issues()
    {
        var (admin, project, typeId, workflowId) = await ProjectWithWorkflowAsync(Basic, AllThree);
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        var start = (await AvailableAsync(admin, issue.Id))!.Single();
        (await ExecuteAsync(admin, issue, start.Id)).EnsureSuccessStatusCode();

        var draft = new
        {
            initialStatusId = ToDo,
            statusIds = new[] { ToDo, Done },
            transitions = new[] { Move("Finish", ToDo, Done) },
        };
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/save-draft", draft, Token)).EnsureSuccessStatusCode();

        var refused = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/publish", new { }, Token);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.StatusMappingRequired);
        (await ReloadAsync(admin, issue.Key))!.Status.Name.ShouldBe("In Progress");

        var published = await admin.Http.PostAsJsonAsync(
            $"/api/v1/workflow/{workflowId}/publish",
            new { statusMappings = new Dictionary<string, Guid> { [InProgress.ToString()] = ToDo } },
            Token);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));

        (await ReloadAsync(admin, issue.Key))!.Status.Name.ShouldBe("To Do");
        (await AvailableAsync(admin, issue.Id))!.Select(item => item.Name).ShouldBe(["Finish"]);
    }

    [Fact]
    public async Task Large_migration_runs_in_the_background()
    {
        var (admin, project, typeId, workflowId) = await ProjectWithWorkflowAsync(Basic, AllThree);
        var issues = new List<IssueDto>();
        for (var index = 0; index < ApiFixture.MigrationSyncLimit + 1; index++)
        {
            issues.Add(await CreateIssueAsync(admin, project.Id, typeId));
        }

        var draft = new { initialStatusId = ToDo, statusIds = new[] { ToDo, InProgress, Done }, transitions = new[] { Move("Begin", ToDo, InProgress), Move("Finish", InProgress, Done) } };
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/save-draft", draft, Token)).EnsureSuccessStatusCode();
        var response = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/publish", new { }, Token);
        (await response.Content.ReadFromJsonAsync<PublishResultDto>(Token))!.MigrationQueued.ShouldBeTrue();

        for (var wait = 0; wait < 100; wait++)
        {
            if ((await AvailableAsync(admin, issues[^1].Id))!.Any(item => item.Name == "Begin"))
            {
                return;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("Issues did not reach the new version.");
    }

    [Fact]
    public async Task Two_concurrent_transitions_let_only_one_win()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issue = await CreateIssueAsync(client, project.Id, await TaskTypeAsync(client, project.Id));
        var available = (await AvailableAsync(client, issue.Id))!;

        var results = await Task.WhenAll(available.Select(item => ExecuteAsync(client, issue, item.Id)));

        results.Count(result => result.IsSuccessStatusCode).ShouldBe(1);
        results.Single(result => !result.IsSuccessStatusCode).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Viewer_sees_no_transitions_and_a_direct_call_is_refused()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var issue = await CreateIssueAsync(owner, project.Id, await TaskTypeAsync(owner, project.Id));
        var move = (await AvailableAsync(owner, issue.Id))!.First();
        var viewer = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = viewer.UserId, role = ProjectRoleNames.Viewer }, Token))
            .EnsureSuccessStatusCode();

        (await AvailableAsync(viewer, issue.Id)).ShouldBeEmpty();
        (await ExecuteAsync(viewer, issue, move.Id)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_space_owns_its_workflows_and_can_only_use_global_ones()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var typeId = await TaskTypeAsync(owner, project.Id);
        var definition = (Guid[] statuses) => new
        {
            initialStatusId = ToDo,
            statusIds = statuses,
            transitions = statuses.Skip(1).Select(status => new { name = $"To {status}", fromStatusId = (Guid?)null, toStatusId = status }).ToArray(),
        };

        var statusResponse = await owner.Http.PostAsJsonAsync("/api/v1/status", new { name = "Space review", category = 1, projectId = project.Id }, Token);
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await statusResponse.Content.ReadAsStringAsync(Token));
        var status = (await statusResponse.Content.ReadFromJsonAsync<StatusDto>(Token))!;

        var created = await owner.Http.PostAsJsonAsync("/api/v1/workflow", new { name = "Own", projectId = project.Id, definition = definition([ToDo, status.Id, Done]) }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var own = (await created.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        (await owner.Http.PostAsJsonAsync($"/api/v1/workflow/{own.Id}/publish", new { }, Token)).EnsureSuccessStatusCode();
        (await owner.Http.PostAsJsonAsync("/api/v1/project-workflow/set", new { projectId = project.Id, issueTypeId = typeId, workflowId = own.Id }, Token)).EnsureSuccessStatusCode();

        var admin = await AdminAsync();
        var globalList = await admin.Http.GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/v1/workflow", Token);
        var global = globalList!.First(item => item.ProjectId == null);
        globalList.ShouldNotContain(item => item.Id == own.Id);
        (await owner.Http.PostAsJsonAsync($"/api/v1/workflow/{global.Id}/save-draft", definition([ToDo, Done]), Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Http.PostAsJsonAsync("/api/v1/workflow", new { name = "Global", definition = definition([ToDo, Done]) }, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var listed = await owner.Http.GetFromJsonAsync<List<WorkflowSummaryDto>>($"/api/v1/workflow?projectId={project.Id}", Token);
        listed!.ShouldContain(item => item.Id == global.Id);
        listed.ShouldContain(item => item.Id == own.Id);

        // Other spaces see neither the workflow nor the status.
        var other = await CreateProjectAsync(admin);
        var otherType = await TaskTypeAsync(admin, other.Id);
        var refused = await admin.Http.PostAsJsonAsync("/api/v1/project-workflow/set", new { projectId = other.Id, issueTypeId = otherType, workflowId = own.Id }, Token);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.OutOfScope);
        var visible = await admin.Http.GetFromJsonAsync<List<StatusDto>>($"/api/v1/status?projectId={other.Id}", Token);
        visible!.ShouldNotContain(item => item.Id == status.Id);

        // A global workflow cannot depend on one space's status.
        var leaky = await admin.Http.PostAsJsonAsync("/api/v1/workflow", new { name = "Leaky", definition = definition([ToDo, status.Id, Done]) }, Token);
        var draft = (await leaky.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        var publish = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{draft.Id}/publish", new { }, Token);
        (await publish.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.OutOfScope);
    }

    [Fact]
    public async Task A_status_only_in_a_draft_is_deleted_without_a_replacement()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var create = await owner.Http.PostAsJsonAsync("/api/v1/status", new { name = "Temporary", category = 1, projectId = project.Id }, Token);
        var status = (await create.Content.ReadFromJsonAsync<StatusDto>(Token))!;
        var definition = new
        {
            initialStatusId = ToDo,
            statusIds = new[] { ToDo, status.Id, Done },
            transitions = new[] { new { name = "Hold", fromStatusId = (Guid?)ToDo, toStatusId = status.Id, rules = Array.Empty<object>() } },
        };
        var workflow = (await (await owner.Http.PostAsJsonAsync("/api/v1/workflow", new { name = "Holds it", projectId = project.Id, definition }, Token)).Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;

        var outsider = await fixture.CreateUserClientAsync(Token);
        (await outsider.Http.DeleteAsync($"/api/v1/status/{status.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var usage = await owner.Http.GetFromJsonAsync<StatusUsageDto>($"/api/v1/status/{status.Id}/usage", Token);
        usage!.ReplacementRequired.ShouldBeFalse();
        (await owner.Http.DeleteAsync($"/api/v1/status/{status.Id}", Token)).EnsureSuccessStatusCode();

        var left = await owner.Http.GetFromJsonAsync<List<StatusDto>>($"/api/v1/status?projectId={project.Id}", Token);
        left!.ShouldNotContain(item => item.Id == status.Id);
        var draft = (await owner.Http.GetFromJsonAsync<WorkflowDetailDto>($"/api/v1/workflow/{workflow.Id}", Token))!.Draft!;
        draft.StatusIds.ShouldNotContain(status.Id);
        draft.Transitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_status_in_use_moves_issues_workflows_and_boards_to_the_replacement()
    {
        var admin = await AdminAsync();
        var project = await CreateProjectAsync(admin);
        var typeId = await TaskTypeAsync(admin, project.Id);
        var create = await admin.Http.PostAsJsonAsync("/api/v1/status", new { name = "Doomed", category = 1, projectId = project.Id }, Token);
        var doomed = (await create.Content.ReadFromJsonAsync<StatusDto>(Token))!;
        var body = new
        {
            name = $"Flow {Guid.NewGuid():N}",
            projectId = project.Id,
            definition = new
            {
                initialStatusId = doomed.Id,
                statusIds = new[] { doomed.Id, InProgress, Done },
                transitions = new[]
                {
                    new { name = "Begin", fromStatusId = (Guid?)doomed.Id, toStatusId = InProgress, rules = Array.Empty<object>() },
                    new { name = "Finish", fromStatusId = (Guid?)doomed.Id, toStatusId = Done, rules = Array.Empty<object>() },
                },
            },
        };
        var workflow = (await (await admin.Http.PostAsJsonAsync("/api/v1/workflow", body, Token)).Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflow.Id}/publish", new { }, Token)).EnsureSuccessStatusCode();
        (await admin.Http.PostAsJsonAsync("/api/v1/project-workflow/set", new { projectId = project.Id, issueTypeId = typeId, workflowId = workflow.Id }, Token)).EnsureSuccessStatusCode();
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        issue.Status.Id.ShouldBe(doomed.Id);

        var view = await admin.Http.GetFromJsonAsync<SprintBoot.Agile.BoardViewDto>($"/api/v1/board/view/{project.Id}", Token);
        var columns = view!.Columns.Select((column, index) => new
        {
            name = column.Name,
            statusIds = (index == 0 ? column.Statuses.Select(status => status.Id).Append(doomed.Id) : column.Statuses.Select(status => status.Id)).ToArray(),
        });
        (await admin.Http.PutAsJsonAsync($"/api/v1/board/columns/{project.Id}", new { columns }, Token)).EnsureSuccessStatusCode();

        var usage = await admin.Http.GetFromJsonAsync<StatusUsageDto>($"/api/v1/status/{doomed.Id}/usage", Token);
        usage!.IssueCount.ShouldBe(1);
        usage.WorkflowCount.ShouldBe(1);
        usage.ReplacementRequired.ShouldBeTrue();

        var refused = await admin.Http.DeleteAsync($"/api/v1/status/{doomed.Id}", Token);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.StatusInUse);
        (await admin.Http.DeleteAsync($"/api/v1/status/{doomed.Id}?replacementId={doomed.Id}", Token)).IsSuccessStatusCode.ShouldBeFalse();

        (await admin.Http.DeleteAsync($"/api/v1/status/{doomed.Id}?replacementId={InProgress}", Token)).EnsureSuccessStatusCode();

        (await ReloadAsync(admin, issue.Key))!.Status.Id.ShouldBe(InProgress);
        var published = (await admin.Http.GetFromJsonAsync<WorkflowDetailDto>($"/api/v1/workflow/{workflow.Id}", Token))!.Published!;
        published.InitialStatusId.ShouldBe(InProgress);
        published.StatusIds.ShouldBe([InProgress, Done], ignoreOrder: true);
        published.Transitions.ShouldHaveSingleItem().Name.ShouldBe("Finish");
        (await AvailableAsync(admin, issue.Id))!.ShouldContain(transition => transition.Name == "Finish");

        var board = await admin.Http.GetFromJsonAsync<SprintBoot.Agile.BoardViewDto>($"/api/v1/board/view/{project.Id}", Token);
        board!.Columns.SelectMany(column => column.Statuses).ShouldNotContain(status => status.Id == doomed.Id);
        board.Columns.SelectMany(column => column.Statuses).Count(status => status.Id == InProgress).ShouldBe(1);
    }

    [Fact]
    public async Task A_transition_without_a_name_reads_from_and_to()
    {
        var (admin, project, typeId, _) = await ProjectWithWorkflowAsync(new[]
        {
            new { name = "", fromStatusId = (Guid?)null, toStatusId = Done, rules = Array.Empty<object>() },
        });
        var issue = await CreateIssueAsync(admin, project.Id, typeId);

        (await AvailableAsync(admin, issue.Id))!.ShouldContain(transition => transition.Name == "Any status to Done");
    }

    [Fact]
    public async Task A_draft_that_matches_the_live_version_is_not_kept()
    {
        var (admin, _, _, workflowId) = await ProjectWithWorkflowAsync(Basic, AllThree);
        async Task<WorkflowDetailDto> SaveAsync(object definition)
        {
            var response = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/save-draft", definition, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
            return (await response.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        }

        // Same behaviour, other transition order and layout: the layout moves onto the live version.
        var same = await SaveAsync(new { initialStatusId = ToDo, statusIds = AllThree, transitions = Enumerable.Reverse(Basic).ToArray(), layoutJson = "{\"positions\":{}}" });
        same.Draft.ShouldBeNull();
        same.Published!.LayoutJson.ShouldBe("{\"positions\":{}}");

        var changed = await SaveAsync(new { initialStatusId = ToDo, statusIds = AllThree, transitions = new[] { Move("Start", ToDo, InProgress), Move("Close", InProgress, Done) } });
        changed.Draft.ShouldNotBeNull();

        (await SaveAsync(new { initialStatusId = ToDo, statusIds = AllThree, transitions = Basic })).Draft.ShouldBeNull();
        var versions = (await admin.Http.GetFromJsonAsync<List<WorkflowVersionDto>>($"/api/v1/workflow/{workflowId}/versions", Token))!;
        versions.ShouldHaveSingleItem().State.ShouldBe(WorkflowVersionState.Published);
    }

    [Fact]
    public async Task Status_lists_of_a_space_follow_its_workflow()
    {
        var (admin, project, _, _) = await ProjectWithWorkflowAsync(new[] { Move("Back", Done, InProgress), Move("Start", ToDo, Done) }, [Done, InProgress, ToDo]);
        var listed = await admin.Http.GetFromJsonAsync<List<StatusDto>>($"/api/v1/status?projectId={project.Id}", Token);
        listed!.Select(status => status.Id).Take(3).ShouldBe([Done, InProgress, ToDo]);
    }

    [Fact]
    public async Task Versions_are_listed_keep_their_status_order_and_can_be_restored()
    {
        var (admin, project, typeId, workflowId) = await ProjectWithWorkflowAsync(new[]
        {
            new { name = "Finish", fromStatusId = (Guid?)ToDo, toStatusId = Done, rules = Array.Empty<object>() },
        });
        var reordered = new
        {
            initialStatusId = ToDo,
            statusIds = new[] { Done, InProgress, ToDo },
            transitions = new[]
            {
                new { name = "Go", fromStatusId = (Guid?)null, toStatusId = InProgress, rules = Array.Empty<object>() },
                new { name = "Close", fromStatusId = (Guid?)null, toStatusId = Done, rules = Array.Empty<object>() },
            },
        };
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/save-draft", reordered, Token)).EnsureSuccessStatusCode();
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/publish", new { }, Token)).EnsureSuccessStatusCode();

        var detail = await admin.Http.GetFromJsonAsync<WorkflowDetailDto>($"/api/v1/workflow/{workflowId}", Token);
        detail!.Published!.StatusIds.ShouldBe([Done, InProgress, ToDo]);
        var issue = await CreateIssueAsync(admin, project.Id, typeId);
        (await AvailableAsync(admin, issue.Id))!.Select(transition => transition.Name).ShouldBe(["Close", "Go"]);

        var versions = (await admin.Http.GetFromJsonAsync<List<WorkflowVersionDto>>($"/api/v1/workflow/{workflowId}/versions", Token))!;
        versions.Select(version => version.State).ShouldBe([WorkflowVersionState.Published, WorkflowVersionState.Archived]);
        versions[0].PublishedAt.ShouldNotBeNull();
        versions[1].TransitionCount.ShouldBe(1);

        var restored = await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflowId}/restore", new { versionId = versions[1].Id }, Token);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync(Token));
        var draft = (await restored.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        draft.Draft!.Transitions.ShouldHaveSingleItem().Name.ShouldBe("Finish");
        draft.Published!.Transitions.Count.ShouldBe(2);

        var (other, _, _, otherWorkflow) = await ProjectWithWorkflowAsync(new[] { new { name = "Finish", fromStatusId = (Guid?)ToDo, toStatusId = Done, rules = Array.Empty<object>() } });
        var foreign = await other.Http.PostAsJsonAsync($"/api/v1/workflow/{otherWorkflow}/restore", new { versionId = versions[1].Id }, Token);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_last_done_status_cannot_be_replaced_by_one_that_is_not_done()
    {
        var admin = await AdminAsync();
        var project = await CreateProjectAsync(admin);
        var create = await admin.Http.PostAsJsonAsync("/api/v1/status", new { name = "Shipped", category = 2, projectId = project.Id }, Token);
        var shipped = (await create.Content.ReadFromJsonAsync<StatusDto>(Token))!;
        var body = new
        {
            name = $"Flow {Guid.NewGuid():N}",
            projectId = project.Id,
            definition = new
            {
                initialStatusId = ToDo,
                statusIds = new[] { ToDo, shipped.Id },
                transitions = new[] { new { name = "Ship", fromStatusId = (Guid?)ToDo, toStatusId = shipped.Id, rules = Array.Empty<object>() } },
            },
        };
        var workflow = (await (await admin.Http.PostAsJsonAsync("/api/v1/workflow", body, Token)).Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflow.Id}/publish", new { }, Token)).EnsureSuccessStatusCode();

        var refused = await admin.Http.DeleteAsync($"/api/v1/status/{shipped.Id}?replacementId={ToDo}", Token);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(WorkflowErrors.NoDoneStatus);
        (await admin.Http.DeleteAsync($"/api/v1/status/{shipped.Id}?replacementId={Done}", Token)).EnsureSuccessStatusCode();
    }
}
