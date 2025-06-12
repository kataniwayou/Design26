using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Manager.OrchestrationSession.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Entities;
using Shared.MassTransit.Commands;

namespace FlowOrchestrator.EntitiesManagers.Infrastructure.MassTransit.Consumers.OrchestrationSession;

public class GetOrchestrationSessionQueryConsumer : IConsumer<GetOrchestrationSessionQuery>
{
    private readonly IOrchestrationSessionEntityRepository _repository;
    private readonly ILogger<GetOrchestrationSessionQueryConsumer> _logger;

    public GetOrchestrationSessionQueryConsumer(
        IOrchestrationSessionEntityRepository repository,
        ILogger<GetOrchestrationSessionQueryConsumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GetOrchestrationSessionQuery> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var query = context.Message;

        _logger.LogInformation("Processing GetOrchestrationSessionQuery. Id: {Id}, CompositeKey: {CompositeKey}",
            query.Id, query.CompositeKey);

        try
        {
            OrchestrationSessionEntity? entity = null;

            if (query.Id.HasValue)
            {
                entity = await _repository.GetByIdAsync(query.Id.Value);
            }
            else if (!string.IsNullOrEmpty(query.CompositeKey))
            {
                entity = await _repository.GetByCompositeKeyAsync(query.CompositeKey);
            }

            stopwatch.Stop();

            if (entity != null)
            {
                _logger.LogInformation("Successfully processed GetOrchestrationSessionQuery. Found entity Id: {Id}, Duration: {Duration}ms",
                    entity.Id, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetOrchestrationSessionQueryResponse
                {
                    Success = true,
                    Entity = entity,
                    Message = "OrchestrationSession entity found"
                });
            }
            else
            {
                _logger.LogInformation("OrchestrationSession entity not found. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                    query.Id, query.CompositeKey, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new GetOrchestrationSessionQueryResponse
                {
                    Success = false,
                    Entity = null,
                    Message = "OrchestrationSession entity not found"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing GetOrchestrationSessionQuery. Id: {Id}, CompositeKey: {CompositeKey}, Duration: {Duration}ms",
                query.Id, query.CompositeKey, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new GetOrchestrationSessionQueryResponse
            {
                Success = false,
                Entity = null,
                Message = $"Error retrieving OrchestrationSession entity: {ex.Message}"
            });
        }
    }
}

public class GetOrchestrationSessionQueryResponse
{
    public bool Success { get; set; }
    public OrchestrationSessionEntity? Entity { get; set; }
    public string Message { get; set; } = string.Empty;
}
