using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Correlation;

namespace Shared.Configuration;

/// <summary>
/// Configuration extension methods for OpenTelemetry observability setup.
/// </summary>
public static class OpenTelemetryConfiguration
{
    /// <summary>
    /// Adds OpenTelemetry observability services to the service collection.
    /// Configures tracing, metrics, and logging with OTLP exporters and correlation ID enrichment.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="serviceName">The service name for OpenTelemetry. If not provided, reads from configuration.</param>
    /// <param name="serviceVersion">The service version for OpenTelemetry. If not provided, reads from configuration.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddOpenTelemetryObservability(this IServiceCollection services, IConfiguration configuration, string? serviceName = null, string? serviceVersion = null)
    {
        // Get service name and version from parameters or configuration, with fallbacks
        serviceName ??= configuration["OpenTelemetry:ServiceName"] ?? "Manager.Schemas";
        serviceVersion ??= configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";

        // Configure resource
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk()
            .AddEnvironmentVariableDetector();

        // Add OpenTelemetry
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = httpContext =>
                        {
                            // Filter out health check requests
                            return !httpContext.Request.Path.StartsWithSegments("/health");
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource($"{serviceName}.*")
                    .AddSource("MassTransit")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317");
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter($"{serviceName}.*")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317");
                    });
            })
            .WithLogging(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317");
                    });

                // Only add console exporter in development when collector is not available
                var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                var useConsoleForDev = configuration.GetValue<bool>("OpenTelemetry:UseConsoleInDevelopment", true);

                if (isDevelopment && useConsoleForDev)
                {
                    builder.AddConsoleExporter();
                }
            });

        // Add correlation ID context service
        services.AddSingleton<ICorrelationIdContext, CorrelationIdContext>();

        return services;
    }
}
