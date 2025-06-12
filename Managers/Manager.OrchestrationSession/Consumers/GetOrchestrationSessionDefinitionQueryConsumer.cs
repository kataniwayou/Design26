using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Manager.OrchestrationSession.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.MassTransit.Commands;

namespace FlowOrchestrator.EntitiesManagers.Infrastructure.MassTransit.Consumers.OrchestrationSession;

public class GetOrchestrationSessionDefinitionQueryConsumer : IConsumer<GetOrchestrationSessionDefinitionQuery>
{
    private readonly IOrchestrationSessionEntityRepository _repository;
    private readonly ILogger<GetOrchestrationSessionDefinitionQueryConsumer> _logger;

    public GetOrchestrationSessionDefinitionQueryConsumer(
        IOrchestrationSessionEntityRepository repository,
        ILogger<GetOrchestrationSessionDefinitionQueryConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GetOrchestrationSessionDefinitionQuery> context)
    {
        var query = context.Message;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Processing GetOrchestrationSessionDefinitionQuery. OrchestrationSessionId: {OrchestrationSessionId}, RequestedBy: {RequestedBy}",
            query.OrchestrationSessionId, query.RequestedBy);

        try
        {
            var entity = await _repository.GetByIdAsync(query.OrchestrationSessionId);

            stopwatch.Stop();

            if (entity != null)
            {
                _logger.LogInformation("Successfully processed GetOrchestrationSessionDefinitionQuery. Found OrchestrationSession Id: {Id}, Definition length: {DefinitionLength}, Duration: {Duration}ms",
                    entity.Id, entity.Definition?.Length ?? 0, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetOrchestrationSessionDefinitionQueryResponse
                {
                    Success = true,
                    Definition = entity.Definition,
                    Message = "OrchestrationSession definition retrieved successfully"
                });
            }
            else
            {
                _logger.LogWarning("OrchestrationSession entity not found. OrchestrationSessionId: {OrchestrationSessionId}, Duration: {Duration}ms",
                    query.OrchestrationSessionId, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetOrchestrationSessionDefinitionQueryResponse
                {
                    Success = false,
                    Definition = null,
                    Message = $"OrchestrationSession entity with ID {query.OrchestrationSessionId} not found"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing GetOrchestrationSessionDefinitionQuery. OrchestrationSessionId: {OrchestrationSessionId}, Duration: {Duration}ms",
                query.OrchestrationSessionId, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new GetOrchestrationSessionDefinitionQueryResponse
            {
                Success = false,
                Definition = null,
                Message = $"Error retrieving OrchestrationSession definition: {ex.Message}"
            });
        }
    }
}
