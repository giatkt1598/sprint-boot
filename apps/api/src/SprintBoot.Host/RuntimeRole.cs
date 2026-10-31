namespace SprintBoot.Host;

/// <summary>Dev chạy All. Production tách API và Worker từ cùng một image.</summary>
public enum RuntimeRole
{
    All,
    Api,
    Worker,
}

public static class RuntimeRoleExtensions
{
    public static RuntimeRole GetRuntimeRole(this IConfiguration configuration)
    {
        var value = configuration["Runtime:Role"];
        if (string.IsNullOrEmpty(value))
        {
            return RuntimeRole.All;
        }

        return Enum.TryParse<RuntimeRole>(value, ignoreCase: true, out var role)
            ? role
            : throw new InvalidOperationException($"Runtime:Role '{value}' không hợp lệ, chỉ nhận All, Api, Worker.");
    }
}
