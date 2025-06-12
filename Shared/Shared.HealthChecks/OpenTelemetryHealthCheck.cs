using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Shared.Correlation;

namespace Shared.HealthChecks;

public class OpenTelemetryHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenTelemetryHealthCheck> _logger;

    public OpenTelemetryHealthCheck(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<OpenTelemetryHealthCheck> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
            
            // For OTLP gRPC endpoint, we'll just check if the host is reachable
            // since gRPC health checks require more complex setup
            var uri = new Uri(endpoint);
            var httpEndpoint = $"http://{uri.Host}:{uri.Port}";
            
            using var response = await _httpClient.GetAsync(httpEndpoint, cancellationToken);
            
            // Even if we get a 404 or other HTTP error, it means the host is reachable
            // which is sufficient for our health check
            _logger.LogDebugWithCorrelation("OpenTelemetry endpoint health check completed. Status: {StatusCode}", response.StatusCode);
            
            return HealthCheckResult.Healthy("OpenTelemetry endpoint is reachable");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarningWithCorrelation(ex, "OpenTelemetry endpoint health check failed");
            return HealthCheckResult.Unhealthy("OpenTelemetry endpoint is not reachable", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarningWithCorrelation(ex, "OpenTelemetry endpoint health check timed out");
            return HealthCheckResult.Unhealthy("OpenTelemetry endpoint health check timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Unexpected error during OpenTelemetry health check");
            return HealthCheckResult.Unhealthy("Unexpected error during OpenTelemetry health check", ex);
        }
    }
}
