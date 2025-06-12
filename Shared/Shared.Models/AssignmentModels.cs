using System.Text.Json.Serialization;

namespace Shared.Models;

/// <summary>
/// Base model for assignment entities
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AddressAssignmentModel), "Address")]
[JsonDerivedType(typeof(DeliveryAssignmentModel), "Delivery")]
public abstract class AssignmentModel
{
    /// <summary>
    /// Entity ID of the assignment
    /// </summary>
    public Guid EntityId { get; set; }
}

/// <summary>
/// Assignment model for address entities
/// </summary>
public class AddressAssignmentModel : AssignmentModel
{
    /// <summary>
    /// Address entity data
    /// </summary>
    public AddressModel Address { get; set; } = new();
}

/// <summary>
/// Assignment model for delivery entities
/// </summary>
public class DeliveryAssignmentModel : AssignmentModel
{
    /// <summary>
    /// Delivery entity data
    /// </summary>
    public DeliveryModel Delivery { get; set; } = new();
}

/// <summary>
/// Model for address entity data with schema definition
/// </summary>
public class AddressModel
{
    /// <summary>
    /// Name of the address entity
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version of the address entity
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Connection string for the address
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Configuration dictionary for the address
    /// </summary>
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Schema definition retrieved from schema manager
    /// </summary>
    public string SchemaDefinition { get; set; } = string.Empty;
}

/// <summary>
/// Model for delivery entity data with schema definition
/// </summary>
public class DeliveryModel
{
    /// <summary>
    /// Name of the delivery entity
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version of the delivery entity
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Payload data for the delivery
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Schema definition retrieved from schema manager
    /// </summary>
    public string SchemaDefinition { get; set; } = string.Empty;
}
