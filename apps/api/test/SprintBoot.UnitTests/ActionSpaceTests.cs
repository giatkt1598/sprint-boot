using Shouldly;
using SprintBoot.Assistant;
using Xunit;

namespace SprintBoot.UnitTests;

public class ActionSpaceTests
{
    [Theory]
    [InlineData("""{"space":"sb","title":"New"}""", "SB")]
    [InlineData("""{"key":"DOC-12","status":"Done"}""", "DOC")]
    [InlineData("""{"keys":["web-3","WEB-4"],"sprint":"active"}""", "WEB")]
    [InlineData("""{"items":[{"key":"API-7","title":"x"}]}""", "API")]
    [InlineData("""{"commentId":"3a24","body":"x"}""", null)]
    [InlineData("""{"key":"not a key"}""", null)]
    [InlineData("not json", null)]
    public void Finds_the_space_an_action_names(string arguments, string? expected) =>
        ActionSpace.KeyOf(arguments).ShouldBe(expected);

    [Fact]
    public void Counts_the_tickets_a_create_made()
    {
        ActionSpace.TicketsCreated("create_ticket", """{"key":"SB-1"}""").ShouldBe(1);
        ActionSpace.TicketsCreated("create_tickets", """{"created":3,"failed":1,"results":[]}""").ShouldBe(3);
        ActionSpace.TicketsCreated("update_ticket", """{"key":"SB-1"}""").ShouldBe(0);
    }
}
