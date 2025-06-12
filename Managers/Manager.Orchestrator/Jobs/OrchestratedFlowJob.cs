using MassTransit;
using Quartz;
using Shared.Correlation;
using Shared.MassTransit.Commands;
using Shared.Models;
using Manager.Orchestrator.Services;

namespace Manager.Orchestrator.Jobs;

/// <summary>
/// Quartz job that executes orchestrated flow entry points on a scheduled basis.
/// This job is responsible for triggering the execution of orchestrated flows based on cron expressions.
/// </summary>
[DisallowConcurrentExecution] // Prevent multiple instances of the same job running simultaneously
public class OrchestratedFlowJob : IJob
{
    private readonly IOrchestrationCacheService _orchestrationCacheService;
    private readonly IBus _bus;
    private readonly ILogger<OrchestratedFlowJob> _logger;

    /// <summary>
    /// Initializes a new instance of the OrchestratedFlowJob class.
    /// </summary>
    /// <param name="orchestrationCacheService">Service for orchestration cache operations</param>
    /// <param name="bus">MassTransit bus for publishing commands</param>
    /// <param name="logger">Logger instance</param>
    public OrchestratedFlowJob(
        IOrchestrationCacheService orchestrationCacheService,
        IBus bus,
        ILogger<OrchestratedFlowJob> logger)
    {
        _orchestrationCacheService = orchestrationCacheService;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    /// Executes the orchestrated flow job.
    /// This method is called by Quartz scheduler based on the configured cron expression.
    /// </summary>
    /// <param name="context">Job execution context containing job data</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task Execute(IJobExecutionContext context)
    {
        // Generate correlation ID for this scheduled execution
        var correlationId = Guid.NewGuid();
        CorrelationIdContext.SetCorrelationIdStatic(correlationId);

        var jobDataMap = context.JobDetail.JobDataMap;
        var orchestratedFlowId = Guid.Parse(jobDataMap.GetString("OrchestratedFlowId")!);

        _logger.LogInformationWithCorrelation("Starting scheduled execution of orchestrated flow {OrchestratedFlowId}", orchestratedFlowId);

        try
        {
            // Get orchestration cache data
            var orchestrationData = await _orchestrationCacheService.GetOrchestrationDataAsync(orchestratedFlowId);
            if (orchestrationData == null)
            {
                _logger.LogWarningWithCorrelation("Orchestration data not found for {OrchestratedFlowId}. Skipping scheduled execution.", orchestratedFlowId);
                return;
            }

            // Get cached entry points (already calculated and validated during orchestration setup)
            var entryPoints = orchestrationData.EntryPoints;

            if (!entryPoints.Any())
            {
                _logger.LogWarningWithCorrelation("No entry points found in cached orchestration data for {OrchestratedFlowId}. Skipping scheduled execution.", orchestratedFlowId);
                return;
            }

            _logger.LogInformationWithCorrelation("Using {EntryPointCount} cached entry points for scheduled execution of {OrchestratedFlowId}", entryPoints.Count, orchestratedFlowId);

            // Execute entry points (using the same logic as manual execution)
            await ExecuteEntryPointsAsync(orchestratedFlowId, entryPoints, orchestrationData, correlationId);

            _logger.LogInformationWithCorrelation("Successfully completed scheduled execution of orchestrated flow {OrchestratedFlowId}", orchestratedFlowId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Failed to execute scheduled orchestrated flow {OrchestratedFlowId}", orchestratedFlowId);
            
            // Re-throw to let Quartz handle the failure (retry policies, etc.)
            throw;
        }
    }

    /// <summary>
    /// Executes the entry points for the orchestrated flow.
    /// This method contains the core logic for publishing ExecuteActivityCommand for each entry point.
    /// </summary>
    /// <param name="orchestratedFlowId">ID of the orchestrated flow</param>
    /// <param name="entryPoints">List of entry point step IDs</param>
    /// <param name="orchestrationData">Cached orchestration data</param>
    /// <param name="correlationId">Correlation ID for this execution</param>
    /// <returns>Task representing the asynchronous operation</returns>
    private async Task ExecuteEntryPointsAsync(
        Guid orchestratedFlowId,
        List<Guid> entryPoints,
        Models.OrchestrationCacheModel orchestrationData,
        Guid correlationId)
    {
        var executionTasks = new List<Task>();
        var publishedCount = 0;

        foreach (var entryPoint in entryPoints)
        {
            try
            {
                // Get assignments for this entry point step
                var assignmentModels = new List<AssignmentModel>();
                if (orchestrationData.AssignmentManager.Assignments.TryGetValue(entryPoint, out List<AssignmentModel>? assignments) && assignments != null)
                {
                    assignmentModels.AddRange(assignments);
                }

                // Get processor ID for this step
                var processorId = orchestrationData.StepManager.StepEntities[entryPoint].ProcessorId;

                // Generate new execution ID for each entry point, but use orchestration correlation ID
                var executionId = Guid.NewGuid();

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

                _logger.LogDebug("Publishing ExecuteActivityCommand for entry point {StepId}. ProcessorId: {ProcessorId}, ExecutionId: {ExecutionId}, AssignmentCount: {AssignmentCount}, CorrelationId: {CorrelationId}",
                    entryPoint, processorId, executionId, assignmentModels.Count, command.CorrelationId);

                // Publish command to processor (async)
                var publishTask = _bus.Publish(command);
                executionTasks.Add(publishTask);
                publishedCount++;

                _logger.LogInformation("Successfully queued ExecuteActivityCommand for entry point {StepId} to processor {ProcessorId} with ExecutionId {ExecutionId}",
                    entryPoint, processorId, executionId);
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(ex, "Failed to execute entry point {StepId} for orchestration {OrchestratedFlowId}",
                    entryPoint, orchestratedFlowId);
                throw;
            }
        }

        // Wait for all publish operations to complete
        if (executionTasks.Any())
        {
            await Task.WhenAll(executionTasks);
            _logger.LogInformationWithCorrelation("Successfully published {PublishedCount} ExecuteActivityCommands for scheduled execution of {OrchestratedFlowId}",
                publishedCount, orchestratedFlowId);
        }
    }
}
