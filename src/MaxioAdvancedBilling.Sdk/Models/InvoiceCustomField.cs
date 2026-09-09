using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

public record InvoiceCustomField
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("owner_id")]
    public int? OwnerId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("owner_type")]
    public CustomFieldOwner? OwnerType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [MinLength(1)]
    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("value")]
    [MinLength(1)]
    public string? Value { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadatum_id")]
    public int? MetadatumId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
