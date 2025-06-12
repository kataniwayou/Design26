using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Shared.Entities.Base;
using Shared.Entities.Validation;

namespace Shared.Entities;

/// <summary>
/// Represents a address entity in the system.
/// Contains Address definition information including version, name, and JSON Address definition.
/// </summary>
public class AddressEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the connection string value.
    /// This provides connection information for the address entity.
    /// </summary>
    [BsonElement("connectionString")]
    [Required(ErrorMessage = "ConnectionString is required")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configuration dictionary.
    /// This contains key-value pairs for additional configuration settings.
    /// </summary>
    [BsonElement("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Gets or sets the schema identifier.
    /// This links the address entity to a specific schema.
    /// </summary>
    [BsonElement("schemaId")]
    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    [Required(ErrorMessage = "SchemaId is required")]
    [NotEmptyGuid(ErrorMessage = "SchemaId cannot be empty")]
    public Guid SchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets the composite key for this address entity.
    /// The composite key is formed by combining the version, name, and connection string.
    /// </summary>
    /// <returns>A string in the format "Version_Name_ConnectionString" that uniquely identifies this address.</returns>
    public override string GetCompositeKey() => $"{Version}_{Name}_{ConnectionString}";
}
