using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Shared.Entities;
using Shared.MassTransit.Events;
using Shared.Repositories.Base;
using Shared.Services;

namespace Manager.OrchestrationSession.Repositories;

public class OrchestrationSessionEntityRepository : BaseRepository<OrchestrationSessionEntity>, IOrchestrationSessionEntityRepository
{
    public OrchestrationSessionEntityRepository(
        IMongoDatabase database, 
        ILogger<BaseRepository<OrchestrationSessionEntity>> logger, 
        IEventPublisher eventPublisher) 
        : base(database, "OrchestrationSession", logger, eventPublisher)
    {
    }

    public async Task<IEnumerable<OrchestrationSessionEntity>> GetByVersionAsync(string version)
    {
        var filter = Builders<OrchestrationSessionEntity>.Filter.Eq(x => x.Version, version);
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task<IEnumerable<OrchestrationSessionEntity>> GetByNameAsync(string name)
    {
        var filter = Builders<OrchestrationSessionEntity>.Filter.Eq(x => x.Name, name);
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task<IEnumerable<OrchestrationSessionEntity>> GetByDefinitionAsync(string definition)
    {
        var filter = Builders<OrchestrationSessionEntity>.Filter.Eq(x => x.Definition, definition);
        return await _collection.Find(filter).ToListAsync();
    }

    protected override void CreateIndexes()
    {
        // Call base implementation if it exists, but since it's abstract, we implement it here

        // Create compound index for composite key (version + name)
        var compositeKeyIndex = Builders<OrchestrationSessionEntity>.IndexKeys
            .Ascending(x => x.Version)
            .Ascending(x => x.Name);
        
        _collection.Indexes.CreateOne(new CreateIndexModel<OrchestrationSessionEntity>(
            compositeKeyIndex, 
            new CreateIndexOptions { Unique = true, Name = "idx_version_name_unique" }));

        // Create individual indexes for search operations
        var versionIndex = Builders<OrchestrationSessionEntity>.IndexKeys.Ascending(x => x.Version);
        _collection.Indexes.CreateOne(new CreateIndexModel<OrchestrationSessionEntity>(
            versionIndex, 
            new CreateIndexOptions { Name = "idx_version" }));

        var nameIndex = Builders<OrchestrationSessionEntity>.IndexKeys.Ascending(x => x.Name);
        _collection.Indexes.CreateOne(new CreateIndexModel<OrchestrationSessionEntity>(
            nameIndex, 
            new CreateIndexOptions { Name = "idx_name" }));

        var definitionIndex = Builders<OrchestrationSessionEntity>.IndexKeys.Ascending(x => x.Definition);
        _collection.Indexes.CreateOne(new CreateIndexModel<OrchestrationSessionEntity>(
            definitionIndex,
            new CreateIndexOptions { Name = "idx_definition" }));
    }

    protected override FilterDefinition<OrchestrationSessionEntity> CreateCompositeKeyFilter(string compositeKey)
    {
        // OrchestrationSessionEntity composite key format: "version_name"
        var parts = compositeKey.Split('_', 2);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid composite key format: {compositeKey}. Expected format: 'version_name'");
        }

        var version = parts[0];
        var name = parts[1];

        return Builders<OrchestrationSessionEntity>.Filter.And(
            Builders<OrchestrationSessionEntity>.Filter.Eq(x => x.Version, version),
            Builders<OrchestrationSessionEntity>.Filter.Eq(x => x.Name, name)
        );
    }

    protected override async Task PublishCreatedEventAsync(OrchestrationSessionEntity entity)
    {
        var createdEvent = new OrchestrationSessionCreatedEvent
        {
            Id = entity.Id,
            Version = entity.Version,
            Name = entity.Name,
            Description = entity.Description,
            Definition = entity.Definition,
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy
        };

        await _eventPublisher.PublishAsync(createdEvent);
    }

    protected override async Task PublishUpdatedEventAsync(OrchestrationSessionEntity entity)
    {
        var updatedEvent = new OrchestrationSessionUpdatedEvent
        {
            Id = entity.Id,
            Version = entity.Version,
            Name = entity.Name,
            Description = entity.Description,
            Definition = entity.Definition,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy
        };

        await _eventPublisher.PublishAsync(updatedEvent);
    }

    protected override async Task PublishDeletedEventAsync(Guid id, string deletedBy)
    {
        var deletedEvent = new OrchestrationSessionDeletedEvent
        {
            Id = id,
            DeletedAt = DateTime.UtcNow,
            DeletedBy = deletedBy
        };

        await _eventPublisher.PublishAsync(deletedEvent);
    }
}
