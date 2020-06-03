using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace VideoIndexerApi.HealthChecks
{
    internal class LivenessHealthCheck : IHealthCheck
    {
        private readonly ILogger<LivenessHealthCheck> _logger;
        public LivenessHealthCheck(ILogger<LivenessHealthCheck> logger)
        {
            _logger = logger;
        }
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.LogInformation("LivenessHealthCheck executed.");
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}