using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SprintBoot.Host.HealthChecks;

public class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Default"));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
