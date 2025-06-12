using Manager.Workflow.Consumers;
using Manager.Workflow.Repositories;
using Manager.Workflow.Services;
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
builder.Services.AddMongoDb<IWorkflowEntityRepository, WorkflowEntityRepository>(builder.Configuration, builder.Configuration["MongoDB:DatabaseName"]!);

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    typeof(CreateWorkflowCommandConsumer),
    typeof(UpdateWorkflowCommandConsumer),
    typeof(DeleteWorkflowCommandConsumer),
    typeof(GetWorkflowQueryConsumer),
    typeof(GetWorkflowStepsQueryConsumer));

// Add HTTP client for OrchestratedFlow validation
builder.Services.AddHttpClient<IOrchestratedFlowValidationService, OrchestratedFlowValidationService>(client =>
{
    var orchestratedFlowManagerUrl = builder.Configuration["Services:OrchestratedFlowManager:BaseUrl"] ?? "http://localhost:60892";
    client.BaseAddress = new Uri(orchestratedFlowManagerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add HTTP client for Workflow validation
builder.Services.AddHttpClient<IManagerHttpClient, ManagerHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add Workflow validation service
builder.Services.AddScoped<IWorkflowValidationService, WorkflowValidationService>();

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
    logger.LogInformation("Starting WorkflowManager API");
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
