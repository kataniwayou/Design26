using Manager.Processor.Consumers;
using Manager.Processor.Repositories;
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
builder.Services.AddMongoDb<IProcessorEntityRepository, ProcessorEntityRepository>(builder.Configuration, builder.Configuration["MongoDB:DatabaseName"]!);

// Add HTTP Client for cross-manager communication
builder.Services.AddHttpClient<Manager.Processor.Services.IManagerHttpClient, Manager.Processor.Services.ManagerHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add Entity Reference Validation Service
builder.Services.AddScoped<Manager.Processor.Services.IEntityReferenceValidator, Manager.Processor.Services.EntityReferenceValidator>();

// Add Schema Validation Service
builder.Services.AddScoped<Manager.Processor.Services.ISchemaValidationService, Manager.Processor.Services.SchemaValidationService>();

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    typeof(CreateProcessorCommandConsumer),
    typeof(UpdateProcessorCommandConsumer),
    typeof(DeleteProcessorCommandConsumer),
    typeof(GetProcessorQueryConsumer),
    typeof(GetProcessorSchemaQueryConsumer));

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
    logger.LogInformation("Starting ProcessorManager API");
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
