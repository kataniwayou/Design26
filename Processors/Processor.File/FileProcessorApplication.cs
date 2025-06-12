using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Correlation;
using Shared.Models;
using Shared.Processor.Application;

namespace Processor.File;

/// <summary>
/// Sample concrete implementation of BaseProcessorApplication
/// Demonstrates how to create a specific processor service
/// The base class now provides a complete default implementation that can be overridden if needed
/// </summary>
public class FileProcessorApplication : BaseProcessorApplication
{
    /// <summary>
    /// Override to add console logging for debugging
    /// </summary>
    protected override void ConfigureLogging(ILoggingBuilder logging)
    {
        // Add console logging for debugging - this will work alongside OpenTelemetry
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Debug);

        // Call base implementation
        base.ConfigureLogging(logging);
    }



    /// <summary>
    /// Concrete implementation of the activity processing logic
    /// This is where the specific processor business logic is implemented
    /// Handles input parsing and validation internally
    /// </summary>
    protected override async Task<ProcessedActivityData> ProcessActivityDataAsync(
        Guid processorId,
        Guid orchestratedFlowEntityId,
        Guid stepId,
        Guid executionId,
        List<AssignmentModel> entities,
        string inputData,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        // Get logger from service provider
        var logger = ServiceProvider.GetRequiredService<ILogger<FileProcessorApplication>>();

        logger.LogInformationWithCorrelation(
            "Processing activity. ProcessorId: {ProcessorId}, StepId: {StepId}, ExecutionId: {ExecutionId}, EntitiesCount: {EntitiesCount}",
            processorId, stepId, executionId, entities.Count);

        // 1. Deserialize input data to local variables based on exampleData structure from ExecuteEntryPointsAsync
        string inputProcessorId = string.Empty;
        string inputOrchestratedFlowEntityId = string.Empty;
        int inputEntitiesProcessed = 0;
        DateTime inputProcessedAt = DateTime.UtcNow;
        string inputProcessingDuration = "0ms";
        bool inputDataReceived = false;
        bool inputMetadataReceived = false;
        string inputSampleData = "No input data";
        string[] inputEntityTypes = Array.Empty<string>();
        object[] inputEntities = Array.Empty<object>();

        try
        {
            if (string.IsNullOrEmpty(inputData))
            {
                logger.LogInformation("No input data provided, using default values");
            }
            else
            {
                // Deserialize the input data based on the exampleData structure
                var inputObject = JsonSerializer.Deserialize<JsonElement>(inputData);

                // Extract top-level properties
                if (inputObject.TryGetProperty("processorId", out var processorIdElement))
                {
                    inputProcessorId = processorIdElement.GetString() ?? string.Empty;
                }

                if (inputObject.TryGetProperty("orchestratedFlowEntityId", out var orchestratedFlowElement))
                {
                    inputOrchestratedFlowEntityId = orchestratedFlowElement.GetString() ?? string.Empty;
                }

                if (inputObject.TryGetProperty("entitiesProcessed", out var entitiesProcessedElement))
                {
                    inputEntitiesProcessed = entitiesProcessedElement.GetInt32();
                }

                // Extract processingDetails properties
                if (inputObject.TryGetProperty("processingDetails", out var processingDetailsElement))
                {
                    if (processingDetailsElement.TryGetProperty("processedAt", out var processedAtElement))
                    {
                        if (DateTime.TryParse(processedAtElement.GetString(), out var parsedDate))
                        {
                            inputProcessedAt = parsedDate;
                        }
                    }

                    if (processingDetailsElement.TryGetProperty("processingDuration", out var durationElement))
                    {
                        inputProcessingDuration = durationElement.GetString() ?? "0ms";
                    }

                    if (processingDetailsElement.TryGetProperty("inputDataReceived", out var dataReceivedElement))
                    {
                        inputDataReceived = dataReceivedElement.GetBoolean();
                    }

                    if (processingDetailsElement.TryGetProperty("inputMetadataReceived", out var metadataReceivedElement))
                    {
                        inputMetadataReceived = metadataReceivedElement.GetBoolean();
                    }

                    if (processingDetailsElement.TryGetProperty("sampleData", out var sampleDataElement))
                    {
                        inputSampleData = sampleDataElement.GetString() ?? "No sample data";
                    }

                    if (processingDetailsElement.TryGetProperty("entityTypes", out var entityTypesElement))
                    {
                        var entityTypesList = new List<string>();
                        foreach (var entityType in entityTypesElement.EnumerateArray())
                        {
                            var typeString = entityType.GetString();
                            if (!string.IsNullOrEmpty(typeString))
                            {
                                entityTypesList.Add(typeString);
                            }
                        }
                        inputEntityTypes = entityTypesList.ToArray();
                    }

                    if (processingDetailsElement.TryGetProperty("entities", out var entitiesElement))
                    {
                        var entitiesList = new List<object>();
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var entityObj = new
                            {
                                entityId = entity.TryGetProperty("entityId", out var entityIdElement) ? entityIdElement.GetString() : string.Empty,
                                type = entity.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : string.Empty,
                                assignmentType = entity.TryGetProperty("assignmentType", out var assignmentTypeElement) ? assignmentTypeElement.GetString() : string.Empty
                            };
                            entitiesList.Add(entityObj);
                        }
                        inputEntities = entitiesList.ToArray();
                    }
                }

                logger.LogInformationWithCorrelation("Successfully deserialized input data. ProcessorId: {ProcessorId}, EntitiesProcessed: {EntitiesProcessed}, SampleData: {SampleData}",
                    inputProcessorId, inputEntitiesProcessed, inputSampleData);
            }
        }
        catch (JsonException ex)
        {
            logger.LogErrorWithCorrelation(ex, "Failed to deserialize input data: {ErrorMessage}", ex.Message);
            throw new InvalidOperationException($"Failed to deserialize input data: {ex.Message}");
        }



        // Simulate some processing time
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

        // Log input data details using deserialized local variables
        logger.LogInformationWithCorrelation(
            "Input data details - ProcessorId: {InputProcessorId}, OrchestratedFlowEntityId: {InputOrchestratedFlowEntityId}, EntitiesProcessed: {InputEntitiesProcessed}, ProcessedAt: {InputProcessedAt}, Duration: {InputProcessingDuration}",
            inputProcessorId, inputOrchestratedFlowEntityId, inputEntitiesProcessed, inputProcessedAt, inputProcessingDuration);

        logger.LogInformationWithCorrelation(
            "Input metadata - DataReceived: {InputDataReceived}, MetadataReceived: {InputMetadataReceived}, SampleData: {InputSampleData}, EntityTypes: {InputEntityTypes}",
            inputDataReceived, inputMetadataReceived, inputSampleData, string.Join(", ", inputEntityTypes));

        // Process entities generically without specific type handling
        string sampleData = "No entities to process";
        if (entities.Any())
        {
            logger.LogInformationWithCorrelation(
                "Processing {EntityCount} entities of types: {EntityTypes}",
                entities.Count,
                string.Join(", ", entities.Select(e => e.GetType().Name).Distinct()));

            // Use first entity for sample processing if available
            var firstEntity = entities.First();
            sampleData = $"Processed entity: {firstEntity.GetType().Name} (EntityId: {firstEntity.EntityId})";
        }

        // Log processing summary including input data information
        logger.LogInformationWithCorrelation(
            "Completed processing entities. Total: {TotalEntities}, SampleData: {SampleData}, InputSampleData: {InputSampleData}",
            entities.Count, sampleData, inputSampleData);


        // Create ProcessedActivityData.Data example for testing schemas between orchestrator and processor
        var processedActivityData = new
        {
            processorId = processorId.ToString(),
            orchestratedFlowEntityId = orchestratedFlowEntityId.ToString(),
            entitiesProcessed = entities.Count,
            processingDetails = new
            {
                processedAt = DateTime.UtcNow,
                processingDuration = "0ms",
                inputDataReceived = true,
                inputMetadataReceived = true,
                sampleData = $"Entry point initialization data for step {stepId}",
                entityTypes = entities.Select(e => e.GetType().Name).Distinct().ToArray(),
                entities = entities.Select(e => new
                {
                    entityId = e.EntityId.ToString(),
                    type = e.GetType().Name,
                    assignmentType = e.GetType().Name
                }).ToArray()
            }
        };




        // Return processed data incorporating deserialized local variables
        return new ProcessedActivityData
        {
            Result = "File processing completed successfully",
            Status = ActivityExecutionStatus.Completed,
            Data = processedActivityData,
            ProcessorName = "EnhancedFileProcessor",
            Version = "3.0",
            ExecutionId = executionId == Guid.Empty ? Guid.NewGuid() : executionId
        };
    }
}