using Shouldly;
using SprintBoot.Issues;
using Xunit;

namespace SprintBoot.UnitTests;

public class RankMathTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("a", null)]
    [InlineData(null, "a")]
    [InlineData("a", "b")]
    [InlineData("a", "a1")]
    [InlineData("az", "b")]
    [InlineData("zz", null)]
    public void Between_sorts_strictly_between_its_neighbours(string? low, string? high)
    {
        var rank = RankMath.Between(low, high);

        if (low is not null)
        {
            string.CompareOrdinal(low, rank).ShouldBeLessThan(0);
        }

        if (high is not null)
        {
            string.CompareOrdinal(rank, high).ShouldBeLessThan(0);
        }
    }

    [Fact]
    public void Repeated_inserts_at_one_spot_stay_ordered()
    {
        var low = RankMath.Between(null, null);
        var high = RankMath.Between(low, null);
        for (var index = 0; index < 500; index++)
        {
            high = RankMath.Between(low, high);
            string.CompareOrdinal(low, high).ShouldBeLessThan(0);
        }
    }

    [Fact]
    public void Sequence_is_ascending()
    {
        var ranks = RankMath.Sequence(1000);

        ranks.ShouldBe(ranks.OrderBy(rank => rank, StringComparer.Ordinal).ToList());
        ranks.Distinct().Count().ShouldBe(1000);
    }
}
