using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Acceptance criteria of Goals: a space's targets, reported on by their owners and changed only by its admins.</summary>
[Collection(ApiCollection.Name)]
public class GoalTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Team(AuthClient Owner, AuthClient Member, ProjectDto Project);

    private async Task<Team> TeamAsync()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var created = await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Goals", key = $"G{Random.Shared.Next(100000, 999999)}", type = 0 }, Token);
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token)).EnsureSuccessStatusCode();
        return new Team(owner, member, project);
    }

    private static async Task<GoalDto> CreateAsync(AuthClient client, Team team, object body)
    {
        var response = await client.Http.PostAsJsonAsync($"/api/v1/goal?projectId={team.Project.Id}", body, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<GoalDto>(Token))!;
    }

    private static async Task<List<GoalDto>> ListAsync(AuthClient client, Team team, bool archived = false) =>
        (await client.Http.GetFromJsonAsync<List<GoalDto>>($"/api/v1/goal?projectId={team.Project.Id}&archived={archived.ToString().ToLowerInvariant()}", Token))!;

    [Fact]
    public async Task Admins_create_and_edit_goals_and_members_read_them()
    {
        var team = await TeamAsync();
        var goal = await CreateAsync(team.Owner, team, new
        {
            name = "  Ship the beta  ",
            description = "<p>Everything <b>works</b></p><script>alert(1)</script>",
            ownerId = team.Member.UserId,
            startDate = "2026-11-01",
            targetDate = "2026-12-31",
        });

        goal.Name.ShouldBe("Ship the beta");
        goal.Status.ShouldBe(GoalStatus.Pending);
        goal.Progress.ShouldBe(0);
        goal.Owner!.Id.ShouldBe(team.Member.UserId);
        goal.Description.ShouldContain("works");
        goal.Description.ShouldNotContain("script");
        goal.CanEdit.ShouldBeTrue();

        var asMember = (await ListAsync(team.Member, team)).ShouldHaveSingleItem();
        asMember.Id.ShouldBe(goal.Id);
        asMember.CanEdit.ShouldBeFalse();

        (await team.Member.Http.PostAsJsonAsync($"/api/v1/goal?projectId={team.Project.Id}", new { name = "Mine" }, Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await team.Member.Http.PutAsJsonAsync($"/api/v1/goal/{goal.Id}", new { name = "Taken over" }, Token)).IsSuccessStatusCode.ShouldBeFalse();

        var edited = await team.Owner.Http.PutAsJsonAsync($"/api/v1/goal/{goal.Id}", new { name = "Ship the beta", targetDate = "2027-01-15" }, Token);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync(Token));
        var reread = (await ListAsync(team.Member, team)).Single();
        reread.TargetDate.ShouldBe(new DateOnly(2027, 1, 15));
        reread.Owner.ShouldBeNull();
    }

    [Fact]
    public async Task A_goal_needs_sane_dates_and_an_owner_from_the_space()
    {
        var team = await TeamAsync();
        var outsider = await fixture.CreateUserClientAsync(Token);

        var backwards = await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal?projectId={team.Project.Id}", new { name = "Wrong way", startDate = "2026-12-01", targetDate = "2026-11-01" }, Token);
        (await backwards.Content.ReadAsStringAsync(Token)).ShouldContain(GoalErrors.DatesInvalid);

        var foreign = await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal?projectId={team.Project.Id}", new { name = "Not ours", ownerId = outsider.UserId }, Token);
        (await foreign.Content.ReadAsStringAsync(Token)).ShouldContain(GoalErrors.OwnerNotMember);

        (await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal?projectId={team.Project.Id}", new { name = "" }, Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await ListAsync(team.Owner, team)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Updates_move_status_and_progress_and_keep_a_history_newest_first()
    {
        var team = await TeamAsync();
        var goal = await CreateAsync(team.Owner, team, new { name = "Grow" });

        (await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal/{goal.Id}/update", new { status = GoalStatus.OnTrack, progress = 40, note = "Good start" }, Token)).EnsureSuccessStatusCode();
        var second = await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal/{goal.Id}/update", new { status = GoalStatus.AtRisk, progress = 55, note = " Slipping " }, Token);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync(Token));

        var current = (await ListAsync(team.Member, team)).Single();
        (current.Status, current.Progress).ShouldBe((GoalStatus.AtRisk, 55));
        current.LastUpdatedAt.ShouldNotBeNull();

        var history = (await team.Member.Http.GetFromJsonAsync<List<GoalUpdateDto>>($"/api/v1/goal/{goal.Id}/updates", Token))!;
        history.Select(item => (item.Status, item.Progress, item.Note)).ShouldBe([(GoalStatus.AtRisk, 55, "Slipping"), (GoalStatus.OnTrack, 40, "Good start")]);
        history[0].Author!.Id.ShouldBe(team.Owner.UserId);

        // Done is complete, whatever the progress said.
        (await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal/{goal.Id}/update", new { status = GoalStatus.Done, progress = 70 }, Token)).EnsureSuccessStatusCode();
        (await ListAsync(team.Member, team)).Single().Progress.ShouldBe(100);

        (await team.Member.Http.PostAsJsonAsync($"/api/v1/goal/{goal.Id}/update", new { status = GoalStatus.OnTrack, progress = 10 }, Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal/{goal.Id}/update", new { status = GoalStatus.OnTrack, progress = 101 }, Token)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Archived_goals_leave_the_list_stay_readable_and_take_no_changes()
    {
        var team = await TeamAsync();
        var goal = await CreateAsync(team.Owner, team, new { name = "Old news" });

        (await team.Member.Http.PostAsync($"/api/v1/goal/{goal.Id}/archive", null, Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await team.Owner.Http.PostAsync($"/api/v1/goal/{goal.Id}/archive", null, Token)).EnsureSuccessStatusCode();

        (await ListAsync(team.Member, team)).ShouldBeEmpty();
        var archived = (await ListAsync(team.Member, team, archived: true)).ShouldHaveSingleItem();
        archived.ArchivedAt.ShouldNotBeNull();
        archived.CanEdit.ShouldBeFalse();

        var edit = await team.Owner.Http.PutAsJsonAsync($"/api/v1/goal/{goal.Id}", new { name = "Changed" }, Token);
        (await edit.Content.ReadAsStringAsync(Token)).ShouldContain(GoalErrors.Archived);
        var update = await team.Owner.Http.PostAsJsonAsync($"/api/v1/goal/{goal.Id}/update", new { status = GoalStatus.OnTrack, progress = 5 }, Token);
        (await update.Content.ReadAsStringAsync(Token)).ShouldContain(GoalErrors.Archived);

        (await team.Owner.Http.PostAsync($"/api/v1/goal/{goal.Id}/unarchive", null, Token)).EnsureSuccessStatusCode();
        (await ListAsync(team.Member, team)).ShouldHaveSingleItem().Id.ShouldBe(goal.Id);
        (await team.Owner.Http.DeleteAsync($"/api/v1/goal/{goal.Id}", Token)).EnsureSuccessStatusCode();
        (await ListAsync(team.Owner, team)).ShouldBeEmpty();
        (await ListAsync(team.Owner, team, archived: true)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Goals_stay_inside_their_space()
    {
        var team = await TeamAsync();
        var other = await TeamAsync();
        var goal = await CreateAsync(team.Owner, team, new { name = "Ours" });

        (await ListAsync(other.Owner, other)).ShouldBeEmpty();
        (await other.Owner.Http.GetAsync($"/api/v1/goal?projectId={team.Project.Id}", Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await other.Owner.Http.PutAsJsonAsync($"/api/v1/goal/{goal.Id}", new { name = "Stolen" }, Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await other.Owner.Http.GetAsync($"/api/v1/goal/{goal.Id}/updates", Token)).IsSuccessStatusCode.ShouldBeFalse();
        (await other.Owner.Http.DeleteAsync($"/api/v1/goal/{goal.Id}", Token)).IsSuccessStatusCode.ShouldBeFalse();
    }
}
