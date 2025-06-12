using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Manager.OrchestrationSession.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Exceptions;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;

namespace FlowOrchestrator.EntitiesManagers.Infrastructure.MassTransit.Consumers.OrchestrationSession;

public class DeleteOrchestrationSessionCommandConsumer : IConsumer<DeleteOrchestrationSessionCommand>
{
    private readonly IOrchestrationSessionEntityRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<DeleteOrchestrationSessionCommandConsumer> _logger;

    public DeleteOrchestrationSessionCommandConsumer(
        IOrchestrationSessionEntityRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<DeleteOrchestrationSessionCommandConsumer> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeleteOrchestrationSessionCommand> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = context.Message;

        _logger.LogInformation("Processing DeleteOrchestrationSessionCommand. Id: {Id}, RequestedBy: {RequestedBy}",
            command.Id, command.RequestedBy);

        try
        {
            var existingEntity = await _repository.GetByIdAsync(command.Id);
            if (existingEntity == null)
            {
                _logger.LogWarning("OrchestrationSession entity not found for deletion. Id: {Id}", command.Id);
                await context.RespondAsync(new DeleteOrchestrationSessionCommandResponse
                {
                    Success = false,
                    Message = $"OrchestrationSession entity with ID {command.Id} not found"
                });
                return;
            }

            var success = await _repository.DeleteAsync(command.Id);

            if (success)
            {
                await _publishEndpoint.Publish(new OrchestrationSessionDeletedEvent
                {
                    Id = command.Id,
                    DeletedAt = DateTime.UtcNow,
                    DeletedBy = command.RequestedBy
                });

                stopwatch.Stop();
                _logger.LogInformation("Successfully processed DeleteOrchestrationSessionCommand. Id: {Id}, Duration: {Duration}ms",
                    command.Id, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new DeleteOrchestrationSessionCommandResponse
                {
                    Success = true,
                    Message = "OrchestrationSession entity deleted successfully"
                });
            }
            else
            {
                stopwatch.Stop();
                _logger.LogWarning("Failed to delete OrchestrationSession entity. Id: {Id}, Duration: {Duration}ms",
                    command.Id, stopwatch.ElapsedMilliseconds);

                await context.RespondAsync(new DeleteOrchestrationSessionCommandResponse
                {
                    Success = false,
                    Message = "Failed to delete OrchestrationSession entity"
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing DeleteOrchestrationSessionCommand. Id: {Id}, Duration: {Duration}ms",
                command.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new DeleteOrchestrationSessionCommandResponse
            {
                Success = false,
                Message = $"Failed to delete OrchestrationSession entity: {ex.Message}"
            });
        }
    }
}

public class DeleteOrchestrationSessionCommandResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
