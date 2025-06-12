using System.Diagnostics;
using System.Text.Json;
using Manager.Orchestrator.Models;
using MassTransit;
using Shared.Correlation;
using Shared.Extensions;
using Shared.MassTransit.Commands;
using Shared.Models;
using Shared.Services;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Main orchestration business logic service
/// </summary>
public class OrchestrationService : IOrchestrationService
{
    private readonly IManagerHttpClient _managerHttpClient;
    private readonly IOrchestrationCacheService _cacheService;
    private readonly ICacheService _rawCacheService;
    private readonly ILogger<OrchestrationService> _logger;
    private readonly IBus _bus;
    private static readonly ActivitySource ActivitySource = new("Manager.Orchestrator.Services");

    public OrchestrationService(
        IManagerHttpClient managerHttpClient,
        IOrchestrationCacheService cacheService,
        ICacheService rawCacheService,
        ILogger<OrchestrationService> logger,
        IBus bus)
    {
        _managerHttpClient = managerHttpClient;
        _cacheService = cacheService;
        _rawCacheService = rawCacheService;
        _logger = logger;
        _bus = bus;
    }

    public async Task StartOrchestrationAsync(Guid orchestratedFlowId)
    {
        await StopOrchestrationAsync(orchestratedFlowId);


        // ✅ Try to get correlation ID from current context, generate new one if this is truly a new workflow start
        var correlationId = GetCurrentCorrelationIdOrGenerate();
        var initiatedBy = "System"; // TODO: Get from user context

        using var activity = ActivitySource.StartActivityWithCorrelation("StartOrchestration");
        activity?.SetTag("orchestratedFlowId", orchestratedFlowId.ToString())
                ?.SetTag("correlationId", correlationId.ToString())
                ?.SetTag("initiatedBy", initiatedBy)
                ?.SetTag("operation", "StartOrchestration");

        var stopwatch = Stopwatch.StartNew();
        var publishedCommands = 0;
        var stepCount = 0;
        var assignmentCount = 0;
        var processorCount = 0;
        var entryPointCount = 0;

        _logger.LogInformationWithCorrelation("Starting orchestration. OrchestratedFlowId: {OrchestratedFlowId}, InitiatedBy: {InitiatedBy}",
            orchestratedFlowId, initiatedBy);

        try
        {
            // Check if orchestration is already active
            activity?.SetTag("step", "0-CheckExisting");
            if (await _cacheService.ExistsAndValidAsync(orchestratedFlowId))
            {
                activity?.SetTag("result", "AlreadyActive");
                _logger.LogInformationWithCorrelation("Orchestration already active. OrchestratedFlowId: {OrchestratedFlowId}",
                    orchestratedFlowId);
                return;
            }

            // Step 1: Retrieve orchestrated flow entity
            activity?.SetTag("step", "1-RetrieveOrchestratedFlow");
            var orchestratedFlow = await _managerHttpClient.GetOrchestratedFlowAsync(orchestratedFlowId);
            if (orchestratedFlow == null)
            {
                throw new InvalidOperationException($"Orchestrated flow not found: {orchestratedFlowId}");
            }

            activity?.SetTag("workflowId", orchestratedFlow.WorkflowId.ToString())
                    ?.SetTag("assignmentIdCount", orchestratedFlow.AssignmentIds.Count);

            _logger.LogInformationWithCorrelation("Retrieved orchestrated flow. OrchestratedFlowId: {OrchestratedFlowId}, WorkflowId: {WorkflowId}, AssignmentCount: {AssignmentCount}",
                orchestratedFlowId, orchestratedFlow.WorkflowId, orchestratedFlow.AssignmentIds.Count);

            // Step 2: Gather all data from managers in parallel
            activity?.SetTag("step", "2-GatherManagerData");
            var stepManagerTask = _managerHttpClient.GetStepManagerDataAsync(orchestratedFlow.WorkflowId);
            var assignmentManagerTask = _managerHttpClient.GetAssignmentManagerDataAsync(orchestratedFlow.AssignmentIds);

            await Task.WhenAll(stepManagerTask, assignmentManagerTask);

            var stepManagerData = await stepManagerTask;
            var assignmentManagerData = await assignmentManagerTask;

            // Collect metrics
            stepCount = stepManagerData.StepIds.Count;
            assignmentCount = assignmentManagerData.Assignments.Values.Sum(list => list.Count);
            processorCount = stepManagerData.ProcessorIds.Count;

            activity?.SetTag("stepCount", stepCount)
                    ?.SetTag("assignmentCount", assignmentCount)
                    ?.SetTag("processorCount", processorCount);

            // Step 3: Create complete orchestration cache model
            activity?.SetTag("step", "3-CreateCacheModel");
            var orchestrationData = new OrchestrationCacheModel
            {
                OrchestratedFlowId = orchestratedFlowId,
                StepManager = stepManagerData,
                AssignmentManager = assignmentManagerData,
                CreatedAt = DateTime.UtcNow
            };

            // Step 4: Store in cache
            activity?.SetTag("step", "4-StoreInCache");
            await _cacheService.StoreOrchestrationDataAsync(orchestratedFlowId, orchestrationData);

            // Step 5: Check processor health
            activity?.SetTag("step", "5-ValidateProcessorHealth");
            await ValidateProcessorHealthAsync(stepManagerData.ProcessorIds);

            // Step 6: Find and validate entry points
            activity?.SetTag("step", "6-FindAndValidateEntryPoints");
            var entryPoints = FindEntryPoints(stepManagerData);
            ValidateEntryPoints(entryPoints);
            entryPointCount = entryPoints.Count;
            activity?.SetTag("entryPointCount", entryPointCount);

            // Step 7: Execute entry point steps
            activity?.SetTag("step", "7-ExecuteEntryPoints");
            await ExecuteEntryPointsAsync(orchestratedFlowId, entryPoints, stepManagerData, assignmentManagerData);
            publishedCommands = entryPoints.Count;

            stopwatch.Stop();
            activity?.SetTag("publishedCommands", publishedCommands)
                    ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                    ?.SetTag("result", "Success")
                    ?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformationWithCorrelation("Successfully started orchestration. OrchestratedFlowId: {OrchestratedFlowId}, StepCount: {StepCount}, AssignmentCount: {AssignmentCount}, ProcessorCount: {ProcessorCount}, EntryPoints: {EntryPointCount}, PublishedCommands: {PublishedCommands}, Duration: {Duration}ms",
                orchestratedFlowId, stepCount, assignmentCount, processorCount, entryPointCount, publishedCommands, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message)
                    ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                    ?.SetTag("error.type", ex.GetType().Name)
                    ?.SetTag("result", "Error");

            _logger.LogErrorWithCorrelation(ex, "Error starting orchestration. OrchestratedFlowId: {OrchestratedFlowId}, Duration: {Duration}ms, ErrorType: {ErrorType}",
                orchestratedFlowId, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    public async Task StopOrchestrationAsync(Guid orchestratedFlowId)
    {
        _logger.LogInformationWithCorrelation("Stopping orchestration. OrchestratedFlowId: {OrchestratedFlowId}",
            orchestratedFlowId);

        try
        {
            // Check if orchestration exists
            var exists = await _cacheService.ExistsAndValidAsync(orchestratedFlowId);
            if (!exists)
            {
                _logger.LogWarningWithCorrelation("Orchestration not found or already expired. OrchestratedFlowId: {OrchestratedFlowId}",
                    orchestratedFlowId);
                return;
            }

            // Remove from cache
            await _cacheService.RemoveOrchestrationDataAsync(orchestratedFlowId);

            _logger.LogInformationWithCorrelation("Successfully stopped orchestration. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Failed to stop orchestration. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
            throw;
        }
    }

    public async Task<OrchestrationStatusModel> GetOrchestrationStatusAsync(Guid orchestratedFlowId)
    {
        _logger.LogDebugWithCorrelation("Getting orchestration status. OrchestratedFlowId: {OrchestratedFlowId}",
            orchestratedFlowId);

        try
        {
            var orchestrationData = await _cacheService.GetOrchestrationDataAsync(orchestratedFlowId);

            if (orchestrationData == null)
            {
                _logger.LogDebugWithCorrelation("Orchestration not found in cache. OrchestratedFlowId: {OrchestratedFlowId}",
                    orchestratedFlowId);

                return new OrchestrationStatusModel
                {
                    OrchestratedFlowId = orchestratedFlowId,
                    IsActive = false,
                    StartedAt = null,
                    ExpiresAt = null,
                    StepCount = 0,
                    AssignmentCount = 0
                };
            }

            var totalAssignments = orchestrationData.AssignmentManager.Assignments.Values.Sum(list => list.Count);

            var status = new OrchestrationStatusModel
            {
                OrchestratedFlowId = orchestratedFlowId,
                IsActive = true,
                StartedAt = orchestrationData.CreatedAt,
                ExpiresAt = orchestrationData.ExpiresAt,
                StepCount = orchestrationData.StepManager.StepIds.Count,
                AssignmentCount = totalAssignments
            };

            _logger.LogDebugWithCorrelation("Retrieved orchestration status. OrchestratedFlowId: {OrchestratedFlowId}, IsActive: {IsActive}, StepCount: {StepCount}, AssignmentCount: {AssignmentCount}",
                orchestratedFlowId, status.IsActive, status.StepCount, status.AssignmentCount);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error getting orchestration status. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
            throw;
        }
    }

    /// <summary>
    /// Validates the health of all processors required for orchestration
    /// </summary>
    /// <param name="processorIds">Collection of processor IDs to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when one or more processors are unhealthy</exception>
    private async Task ValidateProcessorHealthAsync(List<Guid> processorIds)
    {
        _logger.LogDebugWithCorrelation("Validating health of {ProcessorCount} processors", processorIds.Count);

        var unhealthyProcessors = new List<(Guid ProcessorId, string Reason)>();

        foreach (var processorId in processorIds)
        {
            try
            {
                // Get processor health from cache using the same map name as Processor.File
                var healthData = await _rawCacheService.GetAsync("processor-health", processorId.ToString());

                if (string.IsNullOrEmpty(healthData))
                {
                    unhealthyProcessors.Add((processorId, "No health data found in cache"));
                    _logger.LogWarningWithCorrelation("No health data found for processor {ProcessorId}", processorId);
                    continue;
                }

                // Deserialize health entry
                var healthEntry = System.Text.Json.JsonSerializer.Deserialize<ProcessorHealthCacheEntry>(healthData);

                if (healthEntry == null)
                {
                    unhealthyProcessors.Add((processorId, "Failed to deserialize health data"));
                    _logger.LogWarningWithCorrelation("Failed to deserialize health data for processor {ProcessorId}", processorId);
                    continue;
                }

                // Check if health entry has expired
                if (healthEntry.IsExpired)
                {
                    unhealthyProcessors.Add((processorId, $"Health data expired at {healthEntry.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC"));
                    _logger.LogWarningWithCorrelation("Health data expired for processor {ProcessorId}. ExpiresAt: {ExpiresAt}",
                        processorId, healthEntry.ExpiresAt);
                    continue;
                }

                // Check processor health status
                if (healthEntry.Status != HealthStatus.Healthy)
                {
                    unhealthyProcessors.Add((processorId, $"Processor status: {healthEntry.Status}, Message: {healthEntry.Message}"));
                    _logger.LogWarningWithCorrelation("Processor {ProcessorId} is not healthy. Status: {Status}, Message: {Message}",
                        processorId, healthEntry.Status, healthEntry.Message);
                    continue;
                }

                _logger.LogDebugWithCorrelation("Processor {ProcessorId} is healthy. Status: {Status}, LastUpdated: {LastUpdated}",
                    processorId, healthEntry.Status, healthEntry.LastUpdated);
            }
            catch (Exception ex)
            {
                unhealthyProcessors.Add((processorId, $"Error checking health: {ex.Message}"));
                _logger.LogErrorWithCorrelation(ex, "Error checking health for processor {ProcessorId}", processorId);
            }
        }

        // If any processors are unhealthy, fail the orchestration
        if (unhealthyProcessors.Count > 0)
        {
            var errorMessage = $"Failed to start orchestration: {unhealthyProcessors.Count} of {processorIds.Count} processors are unhealthy. " +
                              $"Unhealthy processors: {string.Join(", ", unhealthyProcessors.Select(p => $"{p.ProcessorId} ({p.Reason})"))}";

            _logger.LogErrorWithCorrelation("Processor health validation failed. {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformationWithCorrelation("All {ProcessorCount} processors are healthy", processorIds.Count);
    }

    public async Task<Shared.Models.ProcessorHealthResponse?> GetProcessorHealthAsync(Guid processorId)
    {
        using var activity = ActivitySource.StartActivity("GetProcessorHealth");
        activity?.SetTag("processor.id", processorId.ToString());

        _logger.LogDebugWithCorrelation("Getting health status for processor {ProcessorId}", processorId);

        try
        {
            // Get processor health from cache using the same map name as ProcessorHealthMonitor
            var healthData = await _rawCacheService.GetAsync("processor-health", processorId.ToString());

            if (string.IsNullOrEmpty(healthData))
            {
                _logger.LogDebugWithCorrelation("No health data found for processor {ProcessorId}", processorId);
                return null;
            }

            var healthEntry = JsonSerializer.Deserialize<ProcessorHealthCacheEntry>(healthData);
            if (healthEntry == null)
            {
                _logger.LogWarningWithCorrelation("Failed to deserialize health data for processor {ProcessorId}", processorId);
                return null;
            }

            // Check if health data is expired
            if (healthEntry.IsExpired)
            {
                _logger.LogDebugWithCorrelation("Health data for processor {ProcessorId} is expired. ExpiresAt: {ExpiresAt}",
                    processorId, healthEntry.ExpiresAt);
                return null;
            }

            var response = new Shared.Models.ProcessorHealthResponse
            {
                ProcessorId = healthEntry.ProcessorId,
                Status = healthEntry.Status,
                Message = healthEntry.Message,
                LastUpdated = healthEntry.LastUpdated,
                ExpiresAt = healthEntry.ExpiresAt,
                ReportingPodId = healthEntry.ReportingPodId,
                Uptime = healthEntry.Uptime,
                Metadata = healthEntry.Metadata,
                PerformanceMetrics = healthEntry.PerformanceMetrics,
                HealthChecks = healthEntry.HealthChecks,
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogDebugWithCorrelation("Successfully retrieved health status for processor {ProcessorId}. Status: {Status}, LastUpdated: {LastUpdated}",
                processorId, healthEntry.Status, healthEntry.LastUpdated);

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);
            _logger.LogErrorWithCorrelation(ex, "Error getting health status for processor {ProcessorId}", processorId);
            throw;
        }
    }

    public async Task<Shared.Models.ProcessorsHealthResponse?> GetProcessorsHealthByOrchestratedFlowAsync(Guid orchestratedFlowId)
    {
        using var activity = ActivitySource.StartActivity("GetProcessorsHealthByOrchestratedFlow");
        activity?.SetTag("orchestrated_flow.id", orchestratedFlowId.ToString());

        _logger.LogDebugWithCorrelation("Getting processors health for orchestrated flow {OrchestratedFlowId}", orchestratedFlowId);

        try
        {
            // Get orchestration data from cache to retrieve processor IDs
            var orchestrationData = await _cacheService.GetOrchestrationDataAsync(orchestratedFlowId);

            if (orchestrationData == null)
            {
                _logger.LogDebugWithCorrelation("Orchestrated flow {OrchestratedFlowId} not found in cache", orchestratedFlowId);
                return null;
            }

            if (orchestrationData.IsExpired)
            {
                _logger.LogDebugWithCorrelation("Orchestration data for {OrchestratedFlowId} is expired. ExpiresAt: {ExpiresAt}",
                    orchestratedFlowId, orchestrationData.ExpiresAt);
                return null;
            }

            var processorIds = orchestrationData.StepManager.ProcessorIds;
            _logger.LogDebugWithCorrelation("Found {ProcessorCount} processors for orchestrated flow {OrchestratedFlowId}",
                processorIds.Count, orchestratedFlowId);

            var processorsHealth = new Dictionary<Guid, Shared.Models.ProcessorHealthResponse>();
            var summary = new Shared.Models.ProcessorsHealthSummary
            {
                TotalProcessors = processorIds.Count
            };

            // Get health status for each processor
            foreach (var processorId in processorIds)
            {
                try
                {
                    var healthResponse = await GetProcessorHealthAsync(processorId);

                    if (healthResponse != null)
                    {
                        processorsHealth[processorId] = healthResponse;

                        // Update summary counts
                        switch (healthResponse.Status)
                        {
                            case HealthStatus.Healthy:
                                summary.HealthyProcessors++;
                                break;
                            case HealthStatus.Degraded:
                                summary.DegradedProcessors++;
                                summary.ProblematicProcessors.Add(processorId);
                                break;
                            case HealthStatus.Unhealthy:
                                summary.UnhealthyProcessors++;
                                summary.ProblematicProcessors.Add(processorId);
                                break;
                        }
                    }
                    else
                    {
                        summary.NoHealthDataProcessors++;
                        summary.ProblematicProcessors.Add(processorId);
                        _logger.LogWarningWithCorrelation("No health data found for processor {ProcessorId} in orchestrated flow {OrchestratedFlowId}",
                            processorId, orchestratedFlowId);
                    }
                }
                catch (Exception ex)
                {
                    summary.NoHealthDataProcessors++;
                    summary.ProblematicProcessors.Add(processorId);
                    _logger.LogErrorWithCorrelation(ex, "Error getting health for processor {ProcessorId} in orchestrated flow {OrchestratedFlowId}",
                        processorId, orchestratedFlowId);
                }
            }

            // Determine overall status
            if (summary.UnhealthyProcessors > 0 || summary.NoHealthDataProcessors > 0)
            {
                summary.OverallStatus = HealthStatus.Unhealthy;
            }
            else if (summary.DegradedProcessors > 0)
            {
                summary.OverallStatus = HealthStatus.Degraded;
            }
            else
            {
                summary.OverallStatus = HealthStatus.Healthy;
            }

            var response = new Shared.Models.ProcessorsHealthResponse
            {
                OrchestratedFlowId = orchestratedFlowId,
                Processors = processorsHealth,
                Summary = summary,
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogInformationWithCorrelation("Successfully retrieved health status for {ProcessorCount} processors in orchestrated flow {OrchestratedFlowId}. " +
                                 "Healthy: {HealthyCount}, Degraded: {DegradedCount}, Unhealthy: {UnhealthyCount}, NoData: {NoDataCount}, Overall: {OverallStatus}",
                processorIds.Count, orchestratedFlowId, summary.HealthyProcessors, summary.DegradedProcessors,
                summary.UnhealthyProcessors, summary.NoHealthDataProcessors, summary.OverallStatus);

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);
            _logger.LogErrorWithCorrelation(ex, "Error getting processors health for orchestrated flow {OrchestratedFlowId}", orchestratedFlowId);
            throw;
        }
    }

    /// <summary>
    /// Finds entry points in the workflow by identifying steps that are not referenced as next steps
    /// </summary>
    /// <param name="stepManagerData">Step manager data containing step and next step information</param>
    /// <returns>Collection of step IDs that are entry points</returns>
    private List<Guid> FindEntryPoints(StepManagerModel stepManagerData)
    {
        _logger.LogDebugWithCorrelation("Finding entry points from {StepCount} steps with {NextStepCount} next step references",
            stepManagerData.StepIds.Count, stepManagerData.NextStepIds.Count);

        var entryPoints = new List<Guid>();

        // Find steps that are not referenced as next steps (entry points)
        foreach (var stepId in stepManagerData.StepIds)
        {
            if (!stepManagerData.NextStepIds.Contains(stepId))
            {
                entryPoints.Add(stepId);
                _logger.LogDebugWithCorrelation("Found entry point: {StepId}", stepId);
            }
        }

        _logger.LogInformationWithCorrelation("Found {EntryPointCount} entry points in workflow", entryPoints.Count);
        return entryPoints;
    }

    /// <summary>
    /// Validates that the workflow has valid entry points and is not cyclical
    /// </summary>
    /// <param name="entryPoints">Collection of entry point step IDs</param>
    /// <exception cref="InvalidOperationException">Thrown when workflow has no entry points (indicating cyclicity)</exception>
    private void ValidateEntryPoints(List<Guid> entryPoints)
    {
        _logger.LogDebugWithCorrelation("Validating {EntryPointCount} entry points", entryPoints.Count);

        if (entryPoints.Count == 0)
        {
            var errorMessage = "Failed to start orchestration: No entry points found in workflow. " +
                              "This indicates that the flow is cyclical, which is not allowed. " +
                              "Every workflow must have at least one step that is not referenced as a next step.";

            _logger.LogErrorWithCorrelation("Entry point validation failed: {ErrorMessage}", errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.LogInformationWithCorrelation("Entry point validation passed. Found {EntryPointCount} valid entry points: {EntryPoints}",
            entryPoints.Count, string.Join(", ", entryPoints));
    }

    /// <summary>
    /// Executes all entry point steps by publishing ExecuteActivityCommand to their respective processors
    /// </summary>
    /// <param name="orchestratedFlowId">The orchestrated flow ID</param>
    /// <param name="entryPoints">Collection of entry point step IDs</param>
    /// <param name="stepManagerData">Step manager data containing step information</param>
    /// <param name="assignmentManagerData">Assignment manager data containing assignments</param>
    private async Task ExecuteEntryPointsAsync(Guid orchestratedFlowId, List<Guid> entryPoints,
        StepManagerModel stepManagerData, AssignmentManagerModel assignmentManagerData)
    {
        using var activity = ActivitySource.StartActivity("ExecuteEntryPoints");
        activity?.SetTag("orchestratedFlowId", orchestratedFlowId.ToString())
                ?.SetTag("entryPointCount", entryPoints.Count);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformationWithCorrelation("Executing {EntryPointCount} entry point steps for orchestration {OrchestratedFlowId}",
            entryPoints.Count, orchestratedFlowId);

        var executionTasks = new List<Task>();
        var publishedCount = 0;

        foreach (var entryPoint in entryPoints)
        {
            try
            {
                // Get assignments for this entry point step
                var assignmentModels = new List<AssignmentModel>();
                if (assignmentManagerData.Assignments.TryGetValue(entryPoint, out var assignments))
                {
                    assignmentModels.AddRange(assignments);
                }

                // Get processor ID for this step
                var processorId = stepManagerData.StepEntities[entryPoint].ProcessorId;

                // Generate new execution ID for each entry point, but use orchestration correlation ID
                var executionId = Guid.NewGuid();
                var correlationId = GetCurrentCorrelationIdOrGenerate(); // ✅ Use orchestration correlation ID

                // Write ProcessedActivityData.Data example to processor cache before publishing
                var mapName = processorId.ToString();
                var cacheKey = _rawCacheService.GetCacheKey(orchestratedFlowId, entryPoint, executionId, correlationId);

                // Create ProcessedActivityData.Data example for testing schemas between orchestrator and processor
                var processedActivityData = new
                {
                    processorId = processorId.ToString(),
                    orchestratedFlowEntityId = orchestratedFlowId.ToString(),
                    entitiesProcessed = assignmentModels.Count,
                    processingDetails = new
                    {
                        processedAt = DateTime.UtcNow,
                        processingDuration = "0ms",
                        inputDataReceived = true,
                        inputMetadataReceived = true,
                        sampleData = $"Entry point initialization data for step {entryPoint}",
                        entityTypes = assignmentModels.Select(e => e.GetType().Name).Distinct().ToArray(),
                        entities = assignmentModels.Select(e => new
                        {
                            entityId = e.EntityId.ToString(),
                            type = e.GetType().Name,
                            assignmentType = e.GetType().Name
                        }).ToArray()
                    }
                };

                var exampleData = System.Text.Json.JsonSerializer.Serialize(processedActivityData, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                // Store example data in processor cache
                await _rawCacheService.SetAsync(mapName, cacheKey, exampleData);

                _logger.LogDebugWithCorrelation("Stored example data in processor cache. MapName: {MapName}, Key: {Key}, Data: {Data}",
                    mapName, cacheKey, exampleData);

                // Create ExecuteActivityCommand
                var command = new ExecuteActivityCommand
                {
                    ProcessorId = processorId,
                    OrchestratedFlowEntityId = orchestratedFlowId,
                    StepId = entryPoint,
                    ExecutionId = executionId,
                    Entities = assignmentModels,
                    CorrelationId = correlationId
                };

                _logger.LogDebugWithCorrelation("Publishing ExecuteActivityCommand for entry point {StepId}. ProcessorId: {ProcessorId}, ExecutionId: {ExecutionId}, AssignmentCount: {AssignmentCount}, CorrelationId: {CorrelationId}",
                    entryPoint, processorId, executionId, assignmentModels.Count, command.CorrelationId);

                // Publish command to processor (async)
                var publishTask = _bus.Publish(command);
                executionTasks.Add(publishTask);
                publishedCount++;

                _logger.LogInformationWithCorrelation("Successfully queued ExecuteActivityCommand for entry point {StepId} to processor {ProcessorId} with ExecutionId {ExecutionId}",
                    entryPoint, processorId, executionId);
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(ex, "Failed to execute entry point {StepId} for orchestration {OrchestratedFlowId}",
                    entryPoint, orchestratedFlowId);
                throw;
            }
        }

        // Wait for all commands to be published
        await Task.WhenAll(executionTasks);

        stopwatch.Stop();
        activity?.SetTag("publishedCommands", publishedCount)
                ?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds)
                ?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformationWithCorrelation("Successfully published ExecuteActivityCommand for all {EntryPointCount} entry points. OrchestratedFlowId: {OrchestratedFlowId}, PublishedCommands: {PublishedCommands}, Duration: {Duration}ms",
            entryPoints.Count, orchestratedFlowId, publishedCount, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Gets correlation ID from current context or generates a new one if none exists.
    /// This is appropriate for workflow start operations.
    /// </summary>
    private Guid GetCurrentCorrelationIdOrGenerate()
    {
        // Try to get from Activity baggage first (from HTTP request context)
        var activity = Activity.Current;
        if (activity?.GetBaggageItem("correlation.id") is string baggageValue &&
            Guid.TryParse(baggageValue, out var correlationId))
        {
            return correlationId;
        }

        // Generate new correlation ID for new workflow start
        return Guid.NewGuid();
    }
}
