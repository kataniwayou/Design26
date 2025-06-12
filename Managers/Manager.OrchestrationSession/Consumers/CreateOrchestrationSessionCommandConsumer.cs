using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Manager.OrchestrationSession.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Entities;
using Shared.MassTransit.Commands;
using Shared.MassTransit.Events;

namespace FlowOrchestrator.EntitiesManagers.Infrastructure.MassTransit.Consumers.OrchestrationSession;

public class CreateOrchestrationSessionCommandConsumer : IConsumer<CreateOrchestrationSessionCommand>
{
    private readonly IOrchestrationSessionEntityRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CreateOrchestrationSessionCommandConsumer> _logger;

    public CreateOrchestrationSessionCommandConsumer(
        IOrchestrationSessionEntityRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<CreateOrchestrationSessionCommandConsumer> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CreateOrchestrationSessionCommand> context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = context.Message;

        _logger.LogInformation("Processing CreateOrchestrationSessionCommand. Version: {Version}, Name: {Name}, Definition: {Definition}, RequestedBy: {RequestedBy}",
            command.Version, command.Name, command.Definition, command.RequestedBy);

        try
        {
            var entity = new OrchestrationSessionEntity
            {
                Version = command.Version,
                Name = command.Name,
                Description = command.Description,
                Definition = command.Definition,
                CreatedBy = command.RequestedBy
            };

            var created = await _repository.CreateAsync(entity);

            await _publishEndpoint.Publish(new OrchestrationSessionCreatedEvent
            {
                Id = created.Id,
                Version = created.Version,
                Name = created.Name,
                Description = created.Description,
                Definition = created.Definition,
                CreatedAt = created.CreatedAt,
                CreatedBy = created.CreatedBy
            });

            stopwatch.Stop();
            _logger.LogInformation("Successfully processed CreateOrchestrationSessionCommand. Id: {Id}, Duration: {Duration}ms",
                created.Id, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new CreateOrchestrationSessionCommandResponse
            {
                Success = true,
                Id = created.Id,
                Message = "OrchestrationSession entity created successfully"
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing CreateOrchestrationSessionCommand. Version: {Version}, Name: {Name}, Definition: {Definition}, Duration: {Duration}ms",
                command.Version, command.Name, command.Definition, stopwatch.ElapsedMilliseconds);

            await context.RespondAsync(new CreateOrchestrationSessionCommandResponse
            {
                Success = false,
                Id = Guid.Empty,
                Message = $"Failed to create OrchestrationSession entity: {ex.Message}"
            });
        }
    }
}

public class CreateOrchestrationSessionCommandResponse
{
    public bool Success { get; set; }
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
}
