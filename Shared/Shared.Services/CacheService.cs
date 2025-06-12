using Hazelcast;
using Microsoft.Extensions.Logging;
using Shared.Correlation;
namespace Shared.Services;


/// <summary>
/// Hazelcast-specific cache service implementation
/// </summary>
public class CacheService : ICacheService
{
    private readonly Lazy<Task<IHazelcastClient>> _hazelcastClientFactory;
    private readonly ILogger<CacheService> _logger;
    private readonly System.Diagnostics.ActivitySource _activitySource;

    public CacheService(
        Lazy<Task<IHazelcastClient>> hazelcastClientFactory,
        ILogger<CacheService> logger)
    {
        _hazelcastClientFactory = hazelcastClientFactory;
        _logger = logger;
        _activitySource = new System.Diagnostics.ActivitySource("BaseProcessorApplication.Cache");
    }

    private async Task<IHazelcastClient> GetClientAsync()
    {
        return await _hazelcastClientFactory.Value;
    }

    public async Task<string?> GetAsync(string mapName, string key)
    {
        using var activity = _activitySource.StartActivity("Cache.Get");
        activity?.SetTag("cache.operation", "get")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            var map = await client.GetMapAsync<string, string>(mapName);
            var result = await map.GetAsync(key);

            activity?.SetTag("cache.hit", result != null);

            _logger.LogDebugWithCorrelation("Retrieved data from cache. MapName: {MapName}, Key: {Key}, Found: {Found}",
                mapName, key, result != null);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to retrieve data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task SetAsync(string mapName, string key, string value)
    {
        using var activity = _activitySource.StartActivity("Cache.Set");
        activity?.SetTag("cache.operation", "set")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            var map = await client.GetMapAsync<string, string>(mapName);
            await map.SetAsync(key, value);

            _logger.LogDebugWithCorrelation("Saved data to cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to save data to cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task SetAsync(string mapName, string key, string value, TimeSpan ttl)
    {
        using var activity = _activitySource.StartActivity("Cache.SetWithTtl");
        activity?.SetTag("cache.operation", "set_with_ttl")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key)
                ?.SetTag("cache.ttl_seconds", ttl.TotalSeconds);

        try
        {
            var client = await GetClientAsync();
            var map = await client.GetMapAsync<string, string>(mapName);
            await map.SetAsync(key, value, ttl);

            _logger.LogDebugWithCorrelation("Saved data to cache with TTL. MapName: {MapName}, Key: {Key}, TTL: {TTL}s",
                mapName, key, ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to save data to cache with TTL. MapName: {MapName}, Key: {Key}, TTL: {TTL}s",
                mapName, key, ttl.TotalSeconds);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string mapName, string key)
    {
        using var activity = _activitySource.StartActivity("Cache.Exists");
        activity?.SetTag("cache.operation", "exists")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            var map = await client.GetMapAsync<string, string>(mapName);
            var exists = await map.ContainsKeyAsync(key);

            activity?.SetTag("cache.hit", exists);

            _logger.LogDebugWithCorrelation("Checked cache key existence. MapName: {MapName}, Key: {Key}, Exists: {Exists}",
                mapName, key, exists);

            return exists;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to check cache key existence. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task RemoveAsync(string mapName, string key)
    {
        using var activity = _activitySource.StartActivity("Cache.Remove");
        activity?.SetTag("cache.operation", "remove")
                ?.SetTag("cache.map_name", mapName)
                ?.SetTag("cache.key", key);

        try
        {
            var client = await GetClientAsync();
            var map = await client.GetMapAsync<string, string>(mapName);
            await map.RemoveAsync(key);

            _logger.LogDebugWithCorrelation("Removed data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogErrorWithCorrelation(ex, "Failed to remove data from cache. MapName: {MapName}, Key: {Key}",
                mapName, key);
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            // Simple health check by trying to get a map
            var client = await GetClientAsync();
            var testMap = await client.GetMapAsync<string, string>("health-check");
            return testMap != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithCorrelation(ex, "Hazelcast health check failed");
            return false;
        }
    }

    public string GetCacheKey(Guid orchestratedFlowEntityId, Guid stepId, Guid executionId, Guid correlationId = default)
    {
        return $"{orchestratedFlowEntityId}:{stepId}:{executionId}:{correlationId}";
    }

    public void Dispose()
    {
        _activitySource?.Dispose();
    }
}
