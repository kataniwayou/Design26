using Manager.Assignment.Consumers;
using Manager.Assignment.Repositories;
using Manager.Assignment.Services;
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
builder.Services.AddMongoDb<IAssignmentEntityRepository, AssignmentEntityRepository>(builder.Configuration, builder.Configuration["MongoDB:DatabaseName"]!);

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    typeof(CreateAssignmentCommandConsumer),
    typeof(UpdateAssignmentCommandConsumer),
    typeof(DeleteAssignmentCommandConsumer),
    typeof(GetAssignmentQueryConsumer),
    typeof(GetAssignmentDetailsQueryConsumer));

// Add HTTP client for OrchestratedFlow validation
builder.Services.AddHttpClient<IOrchestratedFlowValidationService, OrchestratedFlowValidationService>(client =>
{
    var orchestratedFlowManagerUrl = builder.Configuration["Services:OrchestratedFlowManager:BaseUrl"] ?? "http://localhost:60892";
    client.BaseAddress = new Uri(orchestratedFlowManagerUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add HTTP client for Assignment validation
builder.Services.AddHttpClient<IManagerHttpClient, ManagerHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add Assignment validation service
builder.Services.AddScoped<IAssignmentValidationService, AssignmentValidationService>();

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
    logger.LogInformation("Starting AssignmentManager API");
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
