using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Manager.OrchestrationSession.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Entities;
using Shared.Exceptions;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;

namespace FlowOrchestrator.EntitiesManagers.Infrastructure.MassTransit.Consumers.OrchestrationSession;

public class UpdateOrchestrationSessionCommandConsumer : IConsumer<UpdateOrchestrationSessionCommand>
{
    private readonly IOrchestrationSessionEntityRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<UpdateOrchestrationSessionCommandConsumer> _logger;

    public UpdateOrchestrationSessionCommandConsumer(
        IOrchestrationSessionEntityRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<UpdateOrchestrationSessionCommandConsumer> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UpdateOrchestrationSessionCommand> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = context.Message;

        _logger.LogInformation("Processing UpdateOrchestrationSessionCommand. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, RequestedBy: {RequestedBy}",
            command.Id, command.Version, command.Name, command.Definition, command.RequestedBy);

        try
        {
            var existingEntity = await _repository.GetByIdAsync(command.Id);
            if (existingEntity == null)
            {
                _logger.LogWarning("OrchestrationSession entity not found for update. Id: {Id}", command.Id);
                await context.RespondAsync(new UpdateOrchestrationSessionCommandResponse
                {
                    Success = false,
                    Message = $"OrchestrationSession entity with ID {command.Id} not found"
                });
                return;
            }

            var entity = new OrchestrationSessionEntity
            {
                Id = command.Id,
                Version = command.Version,
                Name = command.Name,
                Description = command.Description,
                Definition = command.Definition,
                UpdatedBy = command.RequestedBy,
                CreatedAt = existingEntity.CreatedAt,
                CreatedBy = existingEntity.CreatedBy
            };

            var updated = await _repository.UpdateAsync(entity);

            await _publishEndpoint.Publish(new OrchestrationSessionUpdatedEvent
            {
                Id = updated.Id,
                Version = updated.Version,
                Name = updated.Name,
                Description = updated.Description,
                Definition = updated.Definition,
                UpdatedAt = updated.UpdatedAt,
                UpdatedBy = updated.UpdatedBy
            });

            stopwatch.Stop();
            _logger.LogInformation("Successfully processed UpdateOrchestrationSessionCommand. Id: {Id}, Duration: {Duration}ms",
                updated.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new UpdateOrchestrationSessionCommandResponse
            {
                Success = true,
                Message = "OrchestrationSession entity updated successfully"
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing UpdateOrchestrationSessionCommand. Id: {Id}, Duration: {Duration}ms",
                command.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new UpdateOrchestrationSessionCommandResponse
            {
                Success = false,
                Message = $"Failed to update OrchestrationSession entity: {ex.Message}"
            });
        }
    }
}

public class UpdateOrchestrationSessionCommandResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
