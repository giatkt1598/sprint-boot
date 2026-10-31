using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SprintBoot.EntityFrameworkCore;

/// <summary>Chỉ dùng cho lệnh dotnet ef lúc thiết kế, không kết nối DB.</summary>
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
