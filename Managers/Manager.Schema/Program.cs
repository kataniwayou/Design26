using Manager.Schema.Consumers;
using Manager.Schema.Repositories;
using Manager.Schema.Services;
using Shared.Configuration;
using Shared.HealthChecks;

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

// Add MongoDB
builder.Services.AddMongoDb<ISchemaEntityRepository, SchemaEntityRepository>(builder.Configuration, builder.Configuration["MongoDB:DatabaseName"]!);

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    typeof(CreateSchemaCommandConsumer), 
    typeof(UpdateSchemaCommandConsumer), 
    typeof(DeleteSchemaCommandConsumer), 
    typeof(GetSchemaQueryConsumer), 
    typeof(GetSchemaDefinitionQueryConsumer));

// Add HTTP Client for cross-manager communication
builder.Services.AddHttpClient<IManagerHttpClient, ManagerHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add Schema Reference Validation Service
builder.Services.AddScoped<ISchemaReferenceValidator, SchemaReferenceValidator>();

// Add Schema Breaking Change Analysis Service
builder.Services.AddScoped<ISchemaBreakingChangeAnalyzer, SchemaBreakingChangeAnalyzer>();

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
app.UseCors("AllowAll");
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");

try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting SchemaManager API");
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
