using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SprintBoot.EntityFrameworkCore;

/// <summary>Design-time only, for dotnet ef commands. Never connects to a database.</summary>
public class SprintBootDbContextFactory : IDesignTimeDbContextFactory<SprintBootDbContext>
{
    public SprintBootDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SprintBootDbContext>()
            .UseNpgsql("Host=localhost;Database=sprintboot")
            .Options;

        return new SprintBootDbContext(options);
    }
}
