using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Identifiers related to a specific resource.
/// </summary>
public record RelatedIdentifiers
{
    /// <summary>
    /// Order ID related to the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("order_id")]
    [StringLength(20, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public string? OrderId { get; init; }

    /// <summary>
    /// Authorization ID related to the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorization_id")]
    [StringLength(20, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public string? AuthorizationId { get; init; }

    /// <summary>
    /// Capture ID related to the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capture_id")]
    [StringLength(20, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public string? CaptureId { get; init; }
}
