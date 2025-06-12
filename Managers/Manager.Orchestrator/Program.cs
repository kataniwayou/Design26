using System;
using Manager.Orchestrator.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Extensions;
using Shared.Configuration;
using Shared.HealthChecks;
using Shared.Correlation;

var builder = WebApplication.CreateBuilder(args);

// Clear default logging providers - OpenTelemetry will handle logging
builder.Logging.ClearProviders();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add MassTransit with RabbitMQ and register consumers
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    typeof(Manager.Orchestrator.Consumers.ActivityExecutedEventConsumer),
    typeof(Manager.Orchestrator.Consumers.ActivityFailedEventConsumer));

// Add Hazelcast client and cache services
builder.Services.AddHazelcastClient(builder.Configuration);

// Add correlation ID services
builder.Services.AddCorrelationId();

// Add OpenTelemetry with correlation ID enrichment
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"];
var serviceVersion = builder.Configuration["OpenTelemetry:ServiceVersion"];
builder.Services.AddOpenTelemetryObservability(builder.Configuration, serviceName, serviceVersion);

// Add HTTP Client for cross-manager communication
builder.Services.AddHttpClient<IManagerHttpClient, ManagerHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("HttpClient:TimeoutSeconds", 30));
});

// Add orchestration services
builder.Services.AddScoped<IOrchestrationCacheService, OrchestrationCacheService>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();

// Add Health Checks
builder.Services.AddHttpClient<OpenTelemetryHealthCheck>();
builder.Services.AddHealthChecks()
    .AddRabbitMQ(rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMQ:Username"]}:{builder.Configuration["RabbitMQ:Password"]}@{builder.Configuration["RabbitMQ:Host"]}:5672/")
    .AddCheck<OpenTelemetryHealthCheck>("opentelemetry");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add correlation ID middleware early in the pipeline
app.UseCorrelationId();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");

try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting OrchestratorManager API");
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
