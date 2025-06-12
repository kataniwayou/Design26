using Manager.OrchestratedFlow.Consumers;
using Manager.OrchestratedFlow.Repositories;
using Manager.OrchestratedFlow.Services;
using Shared.Configuration;
using Shared.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Clear default logging providers - OpenTelemetry will handle logging
builder.Logging.ClearProviders();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add MongoDB
builder.Services.AddMongoDb<IOrchestratedFlowEntityRepository, OrchestratedFlowEntityRepository>(builder.Configuration, builder.Configuration["MongoDB:DatabaseName"]!);

// Add HTTP clients for validation services
builder.Services.AddHttpClient<IWorkflowValidationService, WorkflowValidationService>(client =>
{
    var workflowManagerUrl = builder.Configuration["Services:WorkflowManager:BaseUrl"] ?? "http://localhost:60890";
    client.BaseAddress = new Uri(workflowManagerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<IAssignmentValidationService, AssignmentValidationService>(client =>
{
    var assignmentManagerUrl = builder.Configuration["Services:AssignmentManager:BaseUrl"] ?? "http://localhost:60888";
    client.BaseAddress = new Uri(assignmentManagerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    typeof(CreateOrchestratedFlowCommandConsumer),
    typeof(UpdateOrchestratedFlowCommandConsumer),
    typeof(DeleteOrchestratedFlowCommandConsumer),
    typeof(GetOrchestratedFlowQueryConsumer));

// Add OpenTelemetry
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"];
var serviceVersion = builder.Configuration["OpenTelemetry:ServiceVersion"];
builder.Services.AddOpenTelemetryObservability(builder.Configuration, serviceName, serviceVersion);

// Add Health Checks
builder.Services.AddHttpClient<OpenTelemetryHealthCheck>();
builder.Services.AddHealthChecks()
    .AddMongoDb(builder.Configuration.GetConnectionString("MongoDB")!)
    .AddRabbitMQ(rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMQ:Username"]}:{builder.Configuration["RabbitMQ:Password"]}@{builder.Configuration["RabbitMQ:Host"]}:{5672}/")
    .AddCheck<OpenTelemetryHealthCheck>("opentelemetry");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
app.UseSwagger();
app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");

try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting OrchestratedFlowManager API");
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
