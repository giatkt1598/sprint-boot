using Microsoft.Extensions.Diagnostics.HealthChecks;
using Volo.Abp.RabbitMQ;

namespace SprintBoot.Host.HealthChecks;

public class RabbitMqHealthCheck(IConnectionPool connectionPool) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = await connectionPool.GetAsync();
        return connection.IsOpen
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed");
    }
}
