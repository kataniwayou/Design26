using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Shared.Correlation;
using Shared.Extensions;
using Shared.Processor.Models;

namespace Shared.Processor.Services;

/// <summary>
/// JSON schema validator implementation using Newtonsoft.Json.Schema
/// </summary>
public class SchemaValidator : ISchemaValidator
{
    private readonly ILogger<SchemaValidator> _logger;
    private readonly SchemaValidationConfiguration _config;
    private readonly ActivitySource _activitySource;
    private readonly Dictionary<string, JSchema> _schemaCache;
    private readonly object _schemaCacheLock = new();

    public SchemaValidator(
        ILogger<SchemaValidator> logger,
        IOptions<SchemaValidationConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _activitySource = new ActivitySource(ActivitySources.Validation);
        _schemaCache = new Dictionary<string, JSchema>();
    }

    public async Task<bool> ValidateAsync(string jsonData, string jsonSchema)
    {
        var result = await ValidateWithDetailsAsync(jsonData, jsonSchema);
        return result.IsValid;
    }

    public async Task<SchemaValidationResult> ValidateWithDetailsAsync(string jsonData, string jsonSchema)
    {
        using var activity = _activitySource.StartActivity("ValidateSchema");
        var stopwatch = Stopwatch.StartNew();

        var result = new SchemaValidationResult();

        try
        {
            // Parse and cache schema
            var schema = await GetOrCreateSchemaAsync(jsonSchema);
            
            // Parse JSON data
            JToken jsonToken;
            try
            {
                jsonToken = JToken.Parse(jsonData);
            }
            catch (JsonReaderException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Invalid JSON format: {ex.Message}");
                result.ErrorPath = ex.Path;
                
                activity?.SetValidationTags(true, false, 1, "unknown", ex.Path);
                
                if (_config.LogValidationErrors)
                {
                    _logger.LogErrorWithCorrelation(ex, "JSON parsing failed during validation");
                }
                
                return result;
            }

            // Validate against schema
            var validationErrors = new List<string>();
            var isValid = jsonToken.IsValid(schema, out IList<string> errorMessages);

            if (!isValid && errorMessages != null)
            {
                validationErrors.AddRange(errorMessages);
                result.ErrorPath = errorMessages.FirstOrDefault()?.Split('.').FirstOrDefault();
            }

            result.IsValid = isValid;
            result.Errors = validationErrors;
            
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            // Set telemetry tags
            activity?.SetValidationTags(
                true, 
                isValid, 
                validationErrors.Count, 
                "json_schema",
                result.ErrorPath);

            // Log results based on configuration
            if (isValid)
            {
                _logger.LogDebugWithCorrelation("Schema validation passed. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                if (_config.LogValidationErrors)
                {
                    _logger.LogErrorWithCorrelation(
                        "Schema validation failed. Errors: {ErrorCount}, Duration: {Duration}ms, Errors: {Errors}",
                        validationErrors.Count, stopwatch.ElapsedMilliseconds, string.Join("; ", validationErrors));
                }
                else if (_config.LogValidationWarnings)
                {
                    _logger.LogWarningWithCorrelation(
                        "Schema validation failed. Errors: {ErrorCount}, Duration: {Duration}ms",
                        validationErrors.Count, stopwatch.ElapsedMilliseconds);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");

            activity?.SetErrorTags(ex)
                    ?.SetValidationTags(true, false, 1, "json_schema");

            _logger.LogErrorWithCorrelation(ex, "Schema validation failed with exception. Duration: {Duration}ms", stopwatch.ElapsedMilliseconds);

            return result;
        }
    }

    private async Task<JSchema> GetOrCreateSchemaAsync(string jsonSchema)
    {
        // Create a cache key based on schema content hash
        var schemaHash = jsonSchema.GetHashCode().ToString();

        lock (_schemaCacheLock)
        {
            if (_schemaCache.TryGetValue(schemaHash, out var cachedSchema))
            {
                return cachedSchema;
            }
        }

        // Parse schema (this is CPU-bound, so we'll run it on a background thread)
        var schema = await Task.Run(() =>
        {
            try
            {
                return JSchema.Parse(jsonSchema);
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(ex, "Failed to parse JSON schema");
                throw new InvalidOperationException($"Invalid JSON schema: {ex.Message}", ex);
            }
        });

        // Cache the parsed schema
        lock (_schemaCacheLock)
        {
            if (!_schemaCache.ContainsKey(schemaHash))
            {
                _schemaCache[schemaHash] = schema;
                
                // Limit cache size to prevent memory issues
                if (_schemaCache.Count > 100)
                {
                    var oldestKey = _schemaCache.Keys.First();
                    _schemaCache.Remove(oldestKey);
                    _logger.LogDebugWithCorrelation("Removed oldest schema from cache. Key: {Key}", oldestKey);
                }
            }
        }

        return schema;
    }

    public void Dispose()
    {
        _activitySource?.Dispose();
    }
}
