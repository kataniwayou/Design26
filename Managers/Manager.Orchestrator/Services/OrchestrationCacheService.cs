using System.Text.Json;
using Manager.Orchestrator.Models;
using Shared.Correlation;
using Shared.Services;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Orchestration cache service implementation following ProcessorHealthMonitor pattern
/// </summary>
public class OrchestrationCacheService : IOrchestrationCacheService
{
    private readonly ICacheService _cacheService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrchestrationCacheService> _logger;
    private readonly string _mapName;
    private readonly TimeSpan _defaultTtl;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly JsonSerializerOptions _jsonOptions;

    public OrchestrationCacheService(
        ICacheService cacheService,
        IConfiguration configuration,
        ILogger<OrchestrationCacheService> logger)
    {
        _cacheService = cacheService;
        _configuration = configuration;
        _logger = logger;

        _mapName = _configuration["OrchestrationCache:MapName"] ?? "orchestration-data";
        _defaultTtl = TimeSpan.FromMinutes(_configuration.GetValue<int>("OrchestrationCache:DefaultTtlMinutes", 60));
        _maxRetries = _configuration.GetValue<int>("OrchestrationCache:MaxRetries", 3);
        _retryDelay = TimeSpan.FromMilliseconds(_configuration.GetValue<int>("OrchestrationCache:RetryDelayMs", 1000));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task StoreOrchestrationDataAsync(Guid orchestratedFlowId, OrchestrationCacheModel orchestrationData, TimeSpan? ttl = null)
    {
        var cacheKey = orchestratedFlowId.ToString();
        var effectiveTtl = ttl ?? _defaultTtl;
        orchestrationData.ExpiresAt = DateTime.UtcNow.Add(effectiveTtl);

        _logger.LogInformationWithCorrelation("Storing orchestration data in cache. OrchestratedFlowId: {OrchestratedFlowId}, TTL: {TTL}",
            orchestratedFlowId, effectiveTtl);

        try
        {
            var cacheValue = JsonSerializer.Serialize(orchestrationData, _jsonOptions);
            await StoreWithRetryAsync(cacheKey, cacheValue, effectiveTtl);

            _logger.LogInformationWithCorrelation("Successfully stored orchestration data in cache. OrchestratedFlowId: {OrchestratedFlowId}, StepCount: {StepCount}, AssignmentCount: {AssignmentCount}",
                orchestratedFlowId, orchestrationData.StepManager.StepIds.Count, orchestrationData.AssignmentManager.Assignments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Failed to store orchestration data in cache. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
            throw;
        }
    }

    public async Task<OrchestrationCacheModel?> GetOrchestrationDataAsync(Guid orchestratedFlowId)
    {
        var cacheKey = orchestratedFlowId.ToString();

        _logger.LogDebugWithCorrelation("Retrieving orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}",
            orchestratedFlowId);

        try
        {
            var cacheValue = await _cacheService.GetAsync(_mapName, cacheKey);

            if (string.IsNullOrEmpty(cacheValue))
            {
                _logger.LogDebugWithCorrelation("No orchestration data found in cache. OrchestratedFlowId: {OrchestratedFlowId}",
                    orchestratedFlowId);
                return null;
            }

            var orchestrationData = JsonSerializer.Deserialize<OrchestrationCacheModel>(cacheValue, _jsonOptions);

            if (orchestrationData == null)
            {
                _logger.LogWarningWithCorrelation("Failed to deserialize orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}",
                    orchestratedFlowId);
                return null;
            }

            // Check if the entry has expired
            if (orchestrationData.IsExpired)
            {
                _logger.LogWarningWithCorrelation("Orchestration data in cache has expired. OrchestratedFlowId: {OrchestratedFlowId}, ExpiresAt: {ExpiresAt}",
                    orchestratedFlowId, orchestrationData.ExpiresAt);
                
                // Remove expired entry
                await RemoveOrchestrationDataAsync(orchestratedFlowId);
                return null;
            }

            _logger.LogDebugWithCorrelation("Successfully retrieved orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}, StepCount: {StepCount}, AssignmentCount: {AssignmentCount}",
                orchestratedFlowId, orchestrationData.StepManager.StepIds.Count, orchestrationData.AssignmentManager.Assignments.Count);

            return orchestrationData;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error retrieving orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
            return null;
        }
    }

    public async Task RemoveOrchestrationDataAsync(Guid orchestratedFlowId)
    {
        var cacheKey = orchestratedFlowId.ToString();

        _logger.LogInformationWithCorrelation("Removing orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}",
            orchestratedFlowId);

        try
        {
            await _cacheService.RemoveAsync(_mapName, cacheKey);

            _logger.LogInformationWithCorrelation("Successfully removed orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error removing orchestration data from cache. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
            throw;
        }
    }

    public async Task<bool> ExistsAndValidAsync(Guid orchestratedFlowId)
    {
        _logger.LogDebugWithCorrelation("Checking if orchestration data exists and is valid. OrchestratedFlowId: {OrchestratedFlowId}",
            orchestratedFlowId);

        try
        {
            var orchestrationData = await GetOrchestrationDataAsync(orchestratedFlowId);
            var exists = orchestrationData != null && !orchestrationData.IsExpired;

            _logger.LogDebugWithCorrelation("Orchestration data existence check result. OrchestratedFlowId: {OrchestratedFlowId}, Exists: {Exists}",
                orchestratedFlowId, exists);

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error checking orchestration data existence. OrchestratedFlowId: {OrchestratedFlowId}",
                orchestratedFlowId);
            return false;
        }
    }

    private async Task StoreWithRetryAsync(string cacheKey, string cacheValue, TimeSpan ttl)
    {
        var retryCount = 0;

        while (retryCount <= _maxRetries)
        {
            try
            {
                await _cacheService.SetAsync(_mapName, cacheKey, cacheValue, ttl);
                _logger.LogDebugWithCorrelation("Successfully stored cache entry. Key: {CacheKey}", cacheKey);
                return; // Success
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount > _maxRetries)
                {
                    _logger.LogErrorWithCorrelation(ex, "Failed to store cache entry after {MaxRetries} retries. Key: {CacheKey}",
                        _maxRetries, cacheKey);
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));

                _logger.LogWarningWithCorrelation(ex, "Failed to store cache entry, retry {RetryCount}/{MaxRetries} in {Delay}ms. Key: {CacheKey}",
                    retryCount, _maxRetries, delay.TotalMilliseconds, cacheKey);

                await Task.Delay(delay);
            }
        }
    }
}
