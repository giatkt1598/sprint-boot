using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Projects;
using SprintBoot.Tickets;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>SB-159: custom fields are defined for the workspace or a space, turned on per ticket type and hold typed values.</summary>
[Collection(ApiCollection.Name)]
public class CustomFieldTests(ApiFixture fixture)
{
    private static int _projectCounter;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewKey() => $"F{Interlocked.Increment(ref _projectCounter)}{Random.Shared.Next(1000, 9999)}";

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<(ProjectDto Project, Dictionary<string, Guid> Types)> SpaceAsync(AuthClient owner)
    {
        var project = await ReadAsync<ProjectDto>(await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Fields", key = NewKey(), type = 0 }, Token));
        var types = await owner.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{project.Id}", Token);
        return (project, types!.ToDictionary(type => type.Name, type => type.Id));
    }

    private static Task<CustomFieldDto> CreateFieldAsync(AuthClient client, object body) =>
        client.Http.PostAsJsonAsync("/api/v1/custom-field", body, Token).ContinueWith(task => ReadAsync<CustomFieldDto>(task.Result), Token).Unwrap();

    private static Task<TicketDto> CreateTicketAsync(AuthClient client, Guid projectId, Guid typeId, object? customFields = null) =>
        client.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId, typeId, title = "Login fails", customFields }, Token)
            .ContinueWith(task => ReadAsync<TicketDto>(task.Result), Token).Unwrap();

    private static Task<HttpResponseMessage> UpdateAsync(AuthClient client, TicketDto ticket, object fields) =>
        client.Http.PutAsJsonAsync($"/api/v1/ticket/{ticket.Id}", new { concurrencyStamp = ticket.ConcurrencyStamp, fields }, Token);

    private static Task<List<SpaceCustomFieldDto>> SetSpaceFieldsAsync(AuthClient client, Guid projectId, params object[] items) =>
        client.Http.PostAsJsonAsync($"/api/v1/custom-field/set-space-fields/{projectId}", new { items }, Token)
            .ContinueWith(task => ReadAsync<List<SpaceCustomFieldDto>>(task.Result), Token).Unwrap();

    [Fact]
    public async Task Only_admins_define_workspace_fields_and_a_space_turns_them_on_per_ticket_type()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync("/api/v1/custom-field", new { name = "Môi trường", type = CustomFieldType.Select, options = new[] { new { label = "Production" } } }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await AdminAsync();
        var field = await CreateFieldAsync(admin, new
        {
            name = "Môi trường",
            type = CustomFieldType.Select,
            options = new[] { new { label = "Production", color = "red" }, new { label = "Staging", color = "amber" } },
        });
        field.Key.ShouldStartWith("moi_truong");
        field.Options.Select(option => option.Label).ShouldBe(["Production", "Staging"]);

        var (project, types) = await SpaceAsync(owner);
        var listed = await owner.Http.GetFromJsonAsync<List<SpaceCustomFieldDto>>($"/api/v1/custom-field/space-fields/{project.Id}", Token);
        listed!.Single(item => item.Field.Id == field.Id).Enabled.ShouldBeFalse();

        var bug = await CreateTicketAsync(owner, project.Id, types["Bug"]);
        bug.CustomFields.ShouldBeEmpty();

        var saved = await SetSpaceFieldsAsync(owner, project.Id, new { fieldId = field.Id, ticketTypeIds = new[] { types["Bug"] } });
        saved.Single(item => item.Field.Id == field.Id).TicketTypeIds.ShouldBe([types["Bug"]]);

        var reread = await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={bug.Key}", Token);
        reread!.CustomFields.ShouldHaveSingleItem().Field.Key.ShouldBe(field.Key);
        (await CreateTicketAsync(owner, project.Id, types["Task"])).CustomFields.ShouldBeEmpty();

        // Renaming keeps the key.
        var renamed = await ReadAsync<CustomFieldDto>(await admin.Http.PutAsJsonAsync($"/api/v1/custom-field/{field.Id}", new
        {
            name = "Environment",
            options = field.Options.Select(option => new { id = option.Id, label = option.Label, color = option.Color }),
        }, Token));
        renamed.Key.ShouldBe(field.Key);
    }

    [Fact]
    public async Task Every_kind_of_value_is_checked_stored_shown_and_kept_in_history()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        async Task<CustomFieldDto> Own(string name, CustomFieldType type, object? options = null, string? defaultValue = null) =>
            await CreateFieldAsync(owner, new { projectId = project.Id, name, type, options, defaultValue });

        var text = await Own("Customer", CustomFieldType.Text);
        var number = await Own("Impact", CustomFieldType.Number, defaultValue: "3");
        var date = await Own("Release date", CustomFieldType.Date);
        var select = await Own("Severity", CustomFieldType.Select, new[] { new { label = "Low" }, new { label = "High" } });
        var multi = await Own("Platforms", CustomFieldType.MultiSelect, new[] { new { label = "Web" }, new { label = "iOS" }, new { label = "Android" } });
        var user = await Own("Reviewer", CustomFieldType.User);
        var check = await Own("Regression", CustomFieldType.Checkbox);
        var url = await Own("Design", CustomFieldType.Url);

        // A space's own field is on at once, for the standard types.
        var ticket = await CreateTicketAsync(owner, project.Id, types["Bug"], new Dictionary<string, object> { [$"cf.{text.Key}"] = "ACME" });
        ticket.CustomFields.Select(item => item.Field.Key).ShouldBe([text.Key, number.Key, date.Key, select.Key, multi.Key, user.Key, check.Key, url.Key]);
        ticket.CustomFields.Single(item => item.Field.Id == text.Id).Text.ShouldBe("ACME");
        ticket.CustomFields.Single(item => item.Field.Id == number.Id).Number.ShouldBe(3m);

        var high = select.Options.Single(option => option.Label == "High");
        var response = await UpdateAsync(owner, ticket, new Dictionary<string, object?>
        {
            [$"cf.{number.Key}"] = 12.5,
            [$"cf.{date.Key}"] = "2026-12-01",
            [$"cf.{select.Key}"] = high.Id,
            [$"cf.{multi.Key}"] = new[] { "Web", "Android" },
            [$"cf.{user.Key}"] = owner.UserId,
            [$"cf.{check.Key}"] = true,
            [$"cf.{url.Key}"] = "https://design.example.com/login",
        });
        var updated = await ReadAsync<TicketDto>(response);
        TicketCustomFieldDto Of(CustomFieldDto field) => updated.CustomFields.Single(item => item.Field.Id == field.Id);
        Of(number).Number.ShouldBe(12.5m);
        Of(date).Date.ShouldBe(new DateOnly(2026, 12, 1));
        Of(select).Options.ShouldHaveSingleItem().Label.ShouldBe("High");
        Of(multi).Options.Select(option => option.Label).ShouldBe(["Web", "Android"]);
        Of(user).User!.Id.ShouldBe(owner.UserId);
        Of(check).Checked.ShouldBe(true);
        Of(url).Text.ShouldBe("https://design.example.com/login");

        foreach (var (key, bad) in new (string, object)[] { (number.Key, "many"), (date.Key, "soon"), (url.Key, "javascript:alert(1)"), (select.Key, "Critical"), (check.Key, "yes"), (user.Key, Guid.NewGuid()) })
        {
            (await UpdateAsync(owner, updated, new Dictionary<string, object> { [$"cf.{key}"] = bad })).StatusCode.ShouldBe(HttpStatusCode.BadRequest, key);
        }

        (await UpdateAsync(owner, updated, new Dictionary<string, object> { ["cf.nope"] = "x" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var activity = await owner.Http.GetFromJsonAsync<List<TicketActivityDto>>($"/api/v1/ticket/{ticket.Id}/activity", Token);
        var severity = activity!.Single(item => item.Field == $"cf.{select.Key}");
        severity.FieldName.ShouldBe("Severity");
        severity.NewValue.ShouldBe("High");
        activity.Single(item => item.Field == $"cf.{user.Key}").NewValue.ShouldNotBe(owner.UserId.ToString());

        // Clearing a field removes its value.
        var cleared = await ReadAsync<TicketDto>(await UpdateAsync(owner, updated, new Dictionary<string, object?> { [$"cf.{multi.Key}"] = null }));
        cleared.CustomFields.Single(item => item.Field.Id == multi.Id).Options.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dropped_option_tickets_use_is_hidden_and_kept_and_a_new_type_hides_values_without_losing_them()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        var field = await CreateFieldAsync(owner, new { projectId = project.Id, name = "Team", type = CustomFieldType.Select, options = new[] { new { label = "Core" }, new { label = "Growth" } } });
        var core = field.Options.Single(option => option.Label == "Core");
        var ticket = await CreateTicketAsync(owner, project.Id, types["Bug"], new Dictionary<string, object> { [field.Key] = core.Id });

        var growthOnly = await ReadAsync<CustomFieldDto>(await owner.Http.PutAsJsonAsync($"/api/v1/custom-field/{field.Id}", new
        {
            name = "Team",
            options = field.Options.Where(option => option.Label == "Growth").Select(option => new { id = option.Id, label = option.Label }),
        }, Token));
        growthOnly.Options.Single(option => option.Id == core.Id).IsHidden.ShouldBeTrue();
        var reread = await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={ticket.Key}", Token);
        reread!.CustomFields.Single().Options.ShouldHaveSingleItem().Id.ShouldBe(core.Id);

        // Nobody can pick the hidden option anew.
        var other = await CreateTicketAsync(owner, project.Id, types["Bug"]);
        (await UpdateAsync(owner, other, new Dictionary<string, object> { [$"cf.{field.Key}"] = core.Id })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Only Bugs show the field: as a Task the ticket hides it, as a Bug again it has its value back.
        await SetSpaceFieldsAsync(owner, project.Id, new { fieldId = field.Id, ticketTypeIds = new[] { types["Bug"] } });
        var asTask = await ReadAsync<TicketDto>(await UpdateAsync(owner, reread, new { type = types["Task"] }));
        asTask.CustomFields.ShouldBeEmpty();
        var asBug = await ReadAsync<TicketDto>(await UpdateAsync(owner, asTask, new { type = types["Bug"] }));
        asBug.CustomFields.Single().Options.ShouldHaveSingleItem().Id.ShouldBe(core.Id);
    }

    [Fact]
    public async Task Space_fields_belong_to_space_admins_and_a_deleted_field_leaves_every_ticket()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token))
            .EnsureSuccessStatusCode();

        (await member.Http.PostAsJsonAsync("/api/v1/custom-field", new { projectId = project.Id, name = "Mine", type = CustomFieldType.Text }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var field = await CreateFieldAsync(owner, new { projectId = project.Id, name = "Customer", type = CustomFieldType.Text });
        var ticket = await CreateTicketAsync(member, project.Id, types["Story"], new Dictionary<string, object> { [field.Key] = "ACME" });
        ticket.CustomFields.ShouldHaveSingleItem().Text.ShouldBe("ACME");

        var listed = await owner.Http.GetFromJsonAsync<List<CustomFieldDto>>($"/api/v1/custom-field?projectId={project.Id}", Token);
        listed!.ShouldHaveSingleItem().UsageCount.ShouldBe(1);

        (await member.Http.DeleteAsync($"/api/v1/custom-field/{field.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Http.DeleteAsync($"/api/v1/custom-field/{field.Id}", Token)).EnsureSuccessStatusCode();
        (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={ticket.Key}", Token))!.CustomFields.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_template_keeps_custom_field_values_for_the_fields_of_its_type()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        var field = await CreateFieldAsync(owner, new { projectId = project.Id, name = "Severity", type = CustomFieldType.Select, options = new[] { new { label = "Low" }, new { label = "High" } } });
        var high = field.Options.Single(option => option.Label == "High");

        var template = await ReadAsync<TicketTemplateDto>(await owner.Http.PostAsJsonAsync($"/api/v1/ticket-template?projectId={project.Id}", new
        {
            ticketTypeId = types["Bug"],
            name = "Bug report",
            content = "<p>Steps</p>",
            isDefault = true,
            customFields = new Dictionary<string, object> { [field.Key] = high.Id },
        }, Token));
        template.CustomFields!.Keys.ShouldBe([field.Key]);

        var unknown = await owner.Http.PostAsJsonAsync($"/api/v1/ticket-template?projectId={project.Id}", new
        {
            ticketTypeId = types["Bug"],
            name = "Other",
            content = "",
            customFields = new Dictionary<string, object> { ["cf.missing"] = "x" },
        }, Token);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Searches_filter_and_sort_by_custom_fields_and_return_them_on_request()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        async Task<CustomFieldDto> Own(string name, CustomFieldType type, object? options = null) =>
            await CreateFieldAsync(owner, new { projectId = project.Id, name, type, options });
        var impact = await Own("Impact", CustomFieldType.Number);
        var due = await Own("Release", CustomFieldType.Date);
        var severity = await Own("Severity", CustomFieldType.Select, new[] { new { label = "Low" }, new { label = "High" } });
        var regression = await Own("Regression", CustomFieldType.Checkbox);
        var customer = await Own("Customer", CustomFieldType.Text);
        var reviewer = await Own("Reviewer", CustomFieldType.User);
        var low = severity.Options.Single(option => option.Label == "Low").Id;
        var high = severity.Options.Single(option => option.Label == "High").Id;

        async Task<TicketDto> Bug(string title, Dictionary<string, object?> values) =>
            await ReadAsync<TicketDto>(await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId = types["Bug"], title, customFields = values }, Token));
        var a = await Bug("A", new() { [impact.Key] = 1, [due.Key] = "2026-01-10", [severity.Key] = high, [regression.Key] = true, [customer.Key] = "ACME Corp", [reviewer.Key] = owner.UserId });
        var b = await Bug("B", new() { [impact.Key] = 5, [due.Key] = "2026-02-10", [severity.Key] = low, [customer.Key] = "Globex" });
        var c = await Bug("C", new() { [impact.Key] = 9, [severity.Key] = high });
        var d = await Bug("D", new());

        async Task<List<string>> Keys(string query) =>
            [.. (await owner.Http.GetFromJsonAsync<TicketSearchResultDto>($"/api/v1/ticket/search?projectId={project.Id}&{query}", Token))!.Items!.Select(item => item.Title)];
        string F(CustomFieldDto field, string rest) => $"custom={Uri.EscapeDataString($"cf.{field.Key}:{rest}")}";

        (await Keys(F(severity, $"in:{high}"))).Order().ShouldBe(["A", "C"]);
        (await Keys($"{F(impact, "gte:2")}&{F(impact, "lte:9")}")).Order().ShouldBe(["B", "C"]);
        (await Keys(F(due, "lte:2026-01-31"))).ShouldBe(["A"]);
        (await Keys(F(regression, "is:true"))).ShouldBe(["A"]);
        (await Keys(F(regression, "is:false"))).Order().ShouldBe(["B", "C", "D"]);
        (await Keys(F(customer, "has:acme"))).ShouldBe(["A"]);
        (await Keys(F(reviewer, $"in:{owner.UserId}"))).ShouldBe(["A"]);
        (await Keys(F(severity, "empty"))).ShouldBe(["D"]);
        (await Keys(F(customer, "any"))).Order().ShouldBe(["A", "B"]);

        (await Keys($"sorting={Uri.EscapeDataString($"cf.{impact.Key} desc")}")).ShouldBe(["C", "B", "A", "D"]);
        (await Keys($"sorting={Uri.EscapeDataString($"cf.{severity.Key}")}")).Take(1).ShouldBe(["B"]);
        (await Keys($"sorting={Uri.EscapeDataString($"cf.{customer.Key} desc")}")).Take(2).ShouldBe(["B", "A"]);

        var withFields = await owner.Http.GetFromJsonAsync<TicketSearchResultDto>($"/api/v1/ticket/search?projectId={project.Id}&includeCustomFields=true&{F(impact, "gte:9")}", Token);
        withFields!.Items!.Single().CustomFields!.Single(item => item.Field.Id == impact.Id).Number.ShouldBe(9m);
        (await owner.Http.GetFromJsonAsync<TicketSearchResultDto>($"/api/v1/ticket/search?projectId={project.Id}", Token))!.Items!.First().CustomFields.ShouldBeNull();

        (await owner.Http.GetAsync($"/api/v1/ticket/search?projectId={project.Id}&custom=cf.nope:any", Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.Http.GetAsync($"/api/v1/ticket/search?projectId={project.Id}&{F(impact, "has:x")}", Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // A space's own field cannot be used outside the space.
        (await owner.Http.GetAsync($"/api/v1/ticket/search?{F(impact, "any")}", Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _ = (a, b, c, d);
    }

    [Fact]
    public async Task A_space_orders_built_in_and_custom_fields_together_and_hides_built_in_ones()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        var field = await CreateFieldAsync(owner, new { projectId = project.Id, name = "Customer", type = CustomFieldType.Text });
        var custom = $"cf.{field.Key}";

        var initial = await owner.Http.GetFromJsonAsync<FieldLayoutDto>($"/api/v1/custom-field/layout/{project.Id}", Token);
        initial!.Order.ShouldBe([.. TicketDetailFields.All, custom]);
        initial.Hidden.ShouldBeEmpty();

        var spaceFields = await owner.Http.GetFromJsonAsync<List<SpaceCustomFieldDto>>($"/api/v1/custom-field/space-fields/{project.Id}", Token);
        var saved = await owner.Http.PostAsJsonAsync($"/api/v1/custom-field/set-space-fields/{project.Id}", new
        {
            items = spaceFields!.Where(item => item.Enabled).Select(item => new { fieldId = item.Field.Id, ticketTypeIds = item.TicketTypeIds }),
            order = new[] { "status", custom, "priority", "assignee", "nope" },
            hidden = new[] { "reporter", "status", "sprint" },
        }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));

        var layout = await owner.Http.GetFromJsonAsync<FieldLayoutDto>($"/api/v1/custom-field/layout/{project.Id}", Token);
        layout!.Order.Take(4).ShouldBe(["status", custom, "priority", "assignee"]);
        layout.Order.ShouldBe(layout.Order.Distinct());
        layout.Order.Length.ShouldBe(TicketDetailFields.All.Length + 1);
        // The status always shows.
        layout.Hidden.ShouldBe(["reporter", "sprint"], ignoreOrder: true);

        // A ticket brings the layout along, so its Details sidebar needs no second request.
        var ticket = await CreateTicketAsync(owner, project.Id, types["Bug"]);
        ticket.FieldLayout.Order.ShouldBe(layout.Order);
        ticket.FieldLayout.Hidden.ShouldBe(layout.Hidden, ignoreOrder: true);
    }

    [Fact]
    public async Task A_tags_field_keeps_free_words_offers_them_again_and_filters_by_them()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        var field = await CreateFieldAsync(owner, new { projectId = project.Id, name = "Components", type = CustomFieldType.Tags });

        var ticket = await CreateTicketAsync(owner, project.Id, types["Bug"], new Dictionary<string, object> { [field.Key] = new[] { "login", " API ", "api", "" } });
        ticket.CustomFields.Single().Tags.ShouldBe(["API", "login"]);
        await CreateTicketAsync(owner, project.Id, types["Bug"], new Dictionary<string, object> { [field.Key] = new[] { "billing" } });

        (await owner.Http.GetFromJsonAsync<List<string>>($"/api/v1/custom-field/{field.Id}/tags/{project.Id}", Token))!.ShouldBe(["API", "billing", "login"]);

        var updated = await ReadAsync<TicketDto>(await UpdateAsync(owner, ticket, new Dictionary<string, object> { [$"cf.{field.Key}"] = new[] { "login", "ui" } }));
        updated.CustomFields.Single().Tags.ShouldBe(["login", "ui"]);
        var activity = await owner.Http.GetFromJsonAsync<List<TicketActivityDto>>($"/api/v1/ticket/{ticket.Id}/activity", Token);
        activity!.Single(item => item.Field == $"cf.{field.Key}").OldValue.ShouldBe("API, login");

        var found = await owner.Http.GetFromJsonAsync<TicketSearchResultDto>($"/api/v1/ticket/search?projectId={project.Id}&custom={Uri.EscapeDataString($"cf.{field.Key}:in:UI,nothing")}", Token);
        found!.Items!.ShouldHaveSingleItem().Id.ShouldBe(ticket.Id);
        (await UpdateAsync(owner, updated, new Dictionary<string, object> { [$"cf.{field.Key}"] = "login" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_required_field_needs_a_value_on_create_and_cannot_be_emptied()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var (project, types) = await SpaceAsync(owner);
        var field = await CreateFieldAsync(owner, new { projectId = project.Id, name = "Customer", type = CustomFieldType.Text, isRequired = true });
        field.IsRequired.ShouldBeTrue();

        var missing = await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId = types["Bug"], title = "No customer" }, Token);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync(Token)).ShouldContain(CustomFieldErrors.Required);

        // Subtasks do not show it, so they need nothing.
        var ticket = await CreateTicketAsync(owner, project.Id, types["Bug"], new Dictionary<string, object> { [field.Key] = "ACME" });
        (await UpdateAsync(owner, ticket, new Dictionary<string, object?> { [$"cf.{field.Key}"] = null })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await UpdateAsync(owner, ticket, new Dictionary<string, object?> { [$"cf.{field.Key}"] = "  " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await UpdateAsync(owner, ticket, new Dictionary<string, object?> { [$"cf.{field.Key}"] = "Globex" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A default fills it in.
        await owner.Http.PutAsJsonAsync($"/api/v1/custom-field/{field.Id}", new { name = "Customer", isRequired = true, defaultValue = "\"Internal\"" }, Token);
        (await CreateTicketAsync(owner, project.Id, types["Bug"])).CustomFields.Single().Text.ShouldBe("Internal");
    }
}
