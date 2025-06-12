
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Shared.Entities;
using Shared.Exceptions;
using Manager.OrchestrationSession.Repositories;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Manager.OrchestrationSession.Controllers;

[ApiController]
[Route("api/[controller]")]
[SwaggerTag("OrchestrationSession management operations")]
public class OrchestrationSessionController : ControllerBase
{
    private readonly IOrchestrationSessionEntityRepository _repository;
    private readonly ILogger<OrchestrationSessionController> _logger;

    public OrchestrationSessionController(
        IOrchestrationSessionEntityRepository repository,
        ILogger<OrchestrationSessionController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrchestrationSessionEntity>>> GetAll()
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformation("Starting GetAll schemas request. User: {User}, RequestId: {RequestId}",
            userContext, HttpContext.TraceIdentifier);

        try
        {
            var entities = await _repository.GetAllAsync();
            var count = entities.Count();

            _logger.LogInformation("Successfully retrieved {Count} OrchestrationSession entities. User: {User}, RequestId: {RequestId}",
                count, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all OrchestrationSession entities. User: {User}, RequestId: {RequestId}",
                userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving OrchestrationSession entities");
        }
    }

    [HttpGet("paged")]
    public async Task<ActionResult<object>> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        var originalPage = page;
        var originalPageSize = pageSize;

        _logger.LogInformation("Starting GetPaged schemas request. Page: {Page}, PageSize: {PageSize}, User: {User}, RequestId: {RequestId}",
            page, pageSize, userContext, HttpContext.TraceIdentifier);

        try
        {
            // Strict validation - return 400 for invalid parameters instead of auto-correcting
            if (page < 1)
            {
                _logger.LogWarning("Invalid page parameter {Page} provided. User: {User}, RequestId: {RequestId}",
                    originalPage, userContext, HttpContext.TraceIdentifier);
                return BadRequest(new
                {
                    error = "Invalid page parameter",
                    message = "Page must be greater than 0",
                    parameter = "page",
                    value = originalPage
                });
            }

            if (pageSize < 1)
            {
                _logger.LogWarning("Invalid pageSize parameter {PageSize} provided. User: {User}, RequestId: {RequestId}",
                    originalPageSize, userContext, HttpContext.TraceIdentifier);
                return BadRequest(new
                {
                    error = "Invalid pageSize parameter",
                    message = "PageSize must be greater than 0",
                    parameter = "pageSize",
                    value = originalPageSize
                });
            }
            else if (pageSize > 100)
            {
                _logger.LogWarning("PageSize parameter {PageSize} exceeds maximum. User: {User}, RequestId: {RequestId}",
                    originalPageSize, userContext, HttpContext.TraceIdentifier);
                return BadRequest(new
                {
                    error = "Invalid pageSize parameter",
                    message = "PageSize cannot exceed 100",
                    parameter = "pageSize",
                    value = originalPageSize,
                    maximum = 100
                });
            }

            var entities = await _repository.GetPagedAsync(page, pageSize);
            var totalCount = await _repository.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var result = new
            {
                data = entities,
                page = page,
                pageSize = pageSize,
                totalCount = totalCount,
                totalPages = totalPages
            };

            _logger.LogInformation("Successfully retrieved paged OrchestrationSession entities. Page: {Page}/{TotalPages}, PageSize: {PageSize}, TotalCount: {TotalCount}, User: {User}, RequestId: {RequestId}",
                page, totalPages, pageSize, totalCount, userContext, HttpContext.TraceIdentifier);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving paged OrchestrationSession entities. Page: {Page}, PageSize: {PageSize}, User: {User}, RequestId: {RequestId}",
                originalPage, originalPageSize, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving paged OrchestrationSession entities");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrchestrationSessionEntity>> GetById(string id)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";

        // Validate GUID format
        if (!Guid.TryParse(id, out Guid guidId))
        {
            _logger.LogWarning("Invalid GUID format provided. Id: {Id}, User: {User}, RequestId: {RequestId}",
                id, userContext, HttpContext.TraceIdentifier);
            return BadRequest($"Invalid GUID format: {id}");
        }

        _logger.LogInformation("Starting GetById OrchestrationSession request. Id: {Id}, User: {User}, RequestId: {RequestId}",
            guidId, userContext, HttpContext.TraceIdentifier);

        try
        {
            var entity = await _repository.GetByIdAsync(guidId);

            if (entity == null)
            {
                _logger.LogWarning("OrchestrationSession entity not found. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return NotFound($"OrchestrationSession entity with ID {guidId} not found");
            }

            _logger.LogInformation("Successfully retrieved OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, User: {User}, RequestId: {RequestId}",
                entity.Id, entity.Version, entity.Name, entity.Definition, userContext, HttpContext.TraceIdentifier);

            return Ok(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OrchestrationSession entity by ID. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving the OrchestrationSession entity");
        }
    }

    [HttpGet("composite/{version}/{name}")]
    public async Task<ActionResult<OrchestrationSessionEntity>> GetByCompositeKey(string version, string name)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        // OrchestrationSessionEntity composite key format: "version_name"
        var compositeKey = $"{version}_{name}";

        _logger.LogInformation("Starting GetByCompositeKey OrchestrationSession request. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
            version, name, compositeKey, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarning("Empty or null version/name provided. Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                    version, name, userContext, HttpContext.TraceIdentifier);
                return BadRequest("Version and name cannot be empty");
            }

            var entity = await _repository.GetByCompositeKeyAsync(compositeKey);

            if (entity == null)
            {
                _logger.LogWarning("OrchestrationSession entity not found by composite key. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                    version, name, compositeKey, userContext, HttpContext.TraceIdentifier);
                return NotFound($"OrchestrationSession entity with version '{version}' and name '{name}' not found");
            }

            _logger.LogInformation("Successfully retrieved OrchestrationSession entity by composite key. CompositeKey: {CompositeKey}, Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, User: {User}, RequestId: {RequestId}",
                compositeKey, entity.Id, entity.Version, entity.Name, entity.Definition, userContext, HttpContext.TraceIdentifier);

            return Ok(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OrchestrationSession entity by composite key. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                version, name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving the OrchestrationSession entity");
        }
    }

    [HttpGet("definition/{definition}")]
    public async Task<ActionResult<IEnumerable<OrchestrationSessionEntity>>> GetByDefinition(string definition)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformation("Starting GetByDefinition OrchestrationSession request. Definition: {Definition}, User: {User}, RequestId: {RequestId}",
            definition, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(definition))
            {
                _logger.LogWarning("Empty or null definition provided. User: {User}, RequestId: {RequestId}",
                    userContext, HttpContext.TraceIdentifier);
                return BadRequest("Definition cannot be empty");
            }

            var entities = await _repository.GetByDefinitionAsync(definition);
            var count = entities.Count();

            _logger.LogInformation("Successfully retrieved {Count} OrchestrationSession entities by definition. Definition: {Definition}, User: {User}, RequestId: {RequestId}",
                count, definition, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OrchestrationSession entities by definition. Definition: {Definition}, User: {User}, RequestId: {RequestId}",
                definition, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving OrchestrationSession entities");
        }
    }

    [HttpGet("version/{version}")]
    public async Task<ActionResult<IEnumerable<OrchestrationSessionEntity>>> GetByVersion(string version)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformation("Starting GetByVersion OrchestrationSession request. Version: {Version}, User: {User}, RequestId: {RequestId}",
            version, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                _logger.LogWarning("Empty or null version provided. User: {User}, RequestId: {RequestId}",
                    userContext, HttpContext.TraceIdentifier);
                return BadRequest("Version cannot be empty");
            }

            var entities = await _repository.GetByVersionAsync(version);
            var count = entities.Count();

            _logger.LogInformation("Successfully retrieved {Count} OrchestrationSession entities by version. Version: {Version}, User: {User}, RequestId: {RequestId}",
                count, version, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OrchestrationSession entities by version. Version: {Version}, User: {User}, RequestId: {RequestId}",
                version, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving OrchestrationSession entities");
        }
    }

    [HttpGet("name/{name}")]
    public async Task<ActionResult<IEnumerable<OrchestrationSessionEntity>>> GetByName(string name)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformation("Starting GetByName OrchestrationSession request. Name: {Name}, User: {User}, RequestId: {RequestId}",
            name, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarning("Empty or null name provided. User: {User}, RequestId: {RequestId}",
                    userContext, HttpContext.TraceIdentifier);
                return BadRequest("Name cannot be empty");
            }

            var entities = await _repository.GetByNameAsync(name);
            var count = entities.Count();

            _logger.LogInformation("Successfully retrieved {Count} OrchestrationSession entities by name. Name: {Name}, User: {User}, RequestId: {RequestId}",
                count, name, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OrchestrationSession entities by name. Name: {Name}, User: {User}, RequestId: {RequestId}",
                name, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving OrchestrationSession entities");
        }
    }

    [HttpPost]
    public async Task<ActionResult<OrchestrationSessionEntity>> Create([FromBody] OrchestrationSessionEntity entity)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        var compositeKey = entity?.GetCompositeKey() ?? "Unknown";

        _logger.LogInformation("Starting Create OrchestrationSession request. CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
            compositeKey, userContext, HttpContext.TraceIdentifier);

        if (entity == null)
        {
            _logger.LogWarning("Null entity provided for OrchestrationSession creation. User: {User}, RequestId: {RequestId}",
                userContext, HttpContext.TraceIdentifier);
            return BadRequest("OrchestrationSession entity cannot be null");
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarning("Invalid model state for OrchestrationSession creation. Errors: {Errors}, User: {User}, RequestId: {RequestId}",
                errorMessage, userContext, HttpContext.TraceIdentifier);
            return BadRequest(ModelState);
        }

        try
        {
            entity!.CreatedBy = userContext;
            entity.Id = Guid.Empty;

            _logger.LogDebug("Creating OrchestrationSession entity with details. Version: {Version}, Name: {Name}, Definition: {Definition}, CreatedBy: {CreatedBy}, User: {User}, RequestId: {RequestId}",
                entity.Version, entity.Name, entity.Definition, entity.CreatedBy, userContext, HttpContext.TraceIdentifier);

            var created = await _repository.CreateAsync(entity);

            if (created.Id == Guid.Empty)
            {
                _logger.LogError("MongoDB failed to generate ID for new OrchestrationSessionEntity. Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                    entity.Version, entity.Name, userContext, HttpContext.TraceIdentifier);
                return StatusCode(500, "Failed to generate entity ID");
            }

            _logger.LogInformation("Successfully created OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                created.Id, created.Version, created.Name, created.Definition, created.GetCompositeKey(), userContext, HttpContext.TraceIdentifier);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DuplicateKeyException ex)
        {
            _logger.LogWarning(ex, "Duplicate key conflict creating OrchestrationSession entity. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OrchestrationSession entity. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while creating the OrchestrationSession entity");
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OrchestrationSessionEntity>> Update(string id, [FromBody] OrchestrationSessionEntity entity)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        var compositeKey = entity?.GetCompositeKey() ?? "Unknown";

        // Validate GUID format
        if (!Guid.TryParse(id, out Guid guidId))
        {
            _logger.LogWarning("Invalid GUID format provided for update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                id, userContext, HttpContext.TraceIdentifier);
            return BadRequest($"Invalid GUID format: {id}");
        }

        _logger.LogInformation("Starting Update OrchestrationSession request. Id: {Id}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
            guidId, compositeKey, userContext, HttpContext.TraceIdentifier);

        if (entity == null)
        {
            _logger.LogWarning("Null entity provided for OrchestrationSession update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);
            return BadRequest("OrchestrationSession entity cannot be null");
        }

        if (guidId != entity.Id)
        {
            _logger.LogWarning("ID mismatch in OrchestrationSession update request. URL ID: {UrlId}, Entity ID: {EntityId}, User: {User}, RequestId: {RequestId}",
                guidId, entity.Id, userContext, HttpContext.TraceIdentifier);
            return BadRequest("ID in URL does not match entity ID");
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarning("Invalid model state for OrchestrationSession update. Id: {Id}, Errors: {Errors}, User: {User}, RequestId: {RequestId}",
                guidId, errorMessage, userContext, HttpContext.TraceIdentifier);
            return BadRequest(ModelState);
        }

        try
        {
            var existingEntity = await _repository.GetByIdAsync(guidId);
            if (existingEntity == null)
            {
                _logger.LogWarning("OrchestrationSession entity not found for update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return NotFound($"OrchestrationSession entity with ID {guidId} not found");
            }

            entity.UpdatedBy = userContext;
            entity.CreatedAt = existingEntity.CreatedAt;
            entity.CreatedBy = existingEntity.CreatedBy;

            _logger.LogDebug("Updating OrchestrationSession entity with details. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, UpdatedBy: {UpdatedBy}, User: {User}, RequestId: {RequestId}",
                entity.Id, entity.Version, entity.Name, entity.Definition, entity.UpdatedBy, userContext, HttpContext.TraceIdentifier);

            var updated = await _repository.UpdateAsync(entity);

            _logger.LogInformation("Successfully updated OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                updated.Id, updated.Version, updated.Name, updated.Definition, updated.GetCompositeKey(), userContext, HttpContext.TraceIdentifier);

            return Ok(updated);
        }
        catch (DuplicateKeyException ex)
        {
            _logger.LogWarning(ex, "Duplicate key conflict updating OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                guidId, entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                guidId, entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while updating the OrchestrationSession entity");
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";

        // Validate GUID format
        if (!Guid.TryParse(id, out Guid guidId))
        {
            _logger.LogWarning("Invalid GUID format provided for delete. Id: {Id}, User: {User}, RequestId: {RequestId}",
                id, userContext, HttpContext.TraceIdentifier);
            return BadRequest($"Invalid GUID format: {id}");
        }

        _logger.LogInformation("Starting Delete OrchestrationSession request. Id: {Id}, User: {User}, RequestId: {RequestId}",
            guidId, userContext, HttpContext.TraceIdentifier);

        try
        {
            var existingEntity = await _repository.GetByIdAsync(guidId);
            if (existingEntity == null)
            {
                _logger.LogWarning("OrchestrationSession entity not found for deletion. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return NotFound($"OrchestrationSession entity with ID {guidId} not found");
            }

            _logger.LogDebug("Deleting OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, User: {User}, RequestId: {RequestId}",
                guidId, existingEntity.Version, existingEntity.Name, existingEntity.Definition, userContext, HttpContext.TraceIdentifier);

            var success = await _repository.DeleteAsync(guidId);

            if (!success)
            {
                _logger.LogError("Failed to delete OrchestrationSession entity. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return StatusCode(500, "Failed to delete OrchestrationSession entity");
            }

            _logger.LogInformation("Successfully deleted OrchestrationSession entity. Id: {Id}, Version: {Version}, Name: {Name}, Definition: {Definition}, User: {User}, RequestId: {RequestId}",
                guidId, existingEntity.Version, existingEntity.Name, existingEntity.Definition, userContext, HttpContext.TraceIdentifier);

            return NoContent();
        }
        
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting OrchestrationSession entity. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while deleting the OrchestrationSession entity");
        }
    }

    [HttpGet("composite")]
    public ActionResult<OrchestrationSessionEntity> GetByCompositeKeyEmpty()
    {
        var userContext = User.Identity?.Name ?? "Anonymous";

        _logger.LogWarning("Empty composite key parameters in GetByCompositeKey request (no parameters provided). User: {User}, RequestId: {RequestId}",
            userContext, HttpContext.TraceIdentifier);

        return BadRequest(new {
            error = "Invalid composite key parameters",
            message = "Both version and name parameters are required",
            parameters = new[] { "version", "name" }
        });
    }
}
