using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using Shared.Entities.Base;
using Shared.Entities.Validation;

namespace Shared.Entities;

/// <summary>
/// Represents a orchestratedflow entity in the system.
/// Contains OrchestratedFlow information including version, name, workflow reference, and assignment references.
/// </summary>
public class OrchestratedFlowEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the workflow identifier.
    /// This references the WorkflowEntity that this orchestrated flow is based on.
    /// </summary>
    [BsonElement("workflowId")]
    [BsonRepresentation(BsonType.String)]
    [Required(ErrorMessage = "WorkflowId is required")]
    [NotEmptyGuid(ErrorMessage = "WorkflowId cannot be empty")]
    public Guid WorkflowId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the collection of assignment identifiers.
    /// This defines the assignments that are part of this orchestrated flow.
    /// Can be empty if no assignments are currently associated.
    /// </summary>
    [BsonElement("assignmentIds")]
    [BsonRepresentation(BsonType.String)]
    [NoEmptyGuids(ErrorMessage = "AssignmentIds cannot contain empty GUIDs")]
    public List<Guid> AssignmentIds { get; set; } = new List<Guid>();
}
