namespace SprintBoot.Host;

/// <summary>Dev runs All. Production splits Api and Worker from the same image.</summary>
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
            : throw new InvalidOperationException($"Invalid Runtime:Role '{value}': expected All, Api or Worker.");
    }
}
