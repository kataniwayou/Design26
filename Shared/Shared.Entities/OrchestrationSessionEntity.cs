using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Shared.Entities.Base;

namespace Shared.Entities;

/// <summary>
/// Represents a orchestrationsession entity in the system.
/// Contains OrchestrationSession definition information including version, name, and JSON OrchestrationSession definition.
/// </summary>
public class OrchestrationSessionEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the JSON OrchestrationSession definition.
    /// This contains the actual OrchestrationSession structure and validation rules.
    /// </summary>
    [BsonElement("definition")]
    [Required(ErrorMessage = "Definition is required")]
    public string Definition { get; set; } = string.Empty;
}
