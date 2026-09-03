using System.Collections.Generic;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation.Attributes;

namespace PayPal.Models;

/// <summary>
/// The error details.
/// </summary>
public record SearchError
{
    /// <summary>
    /// The human-readable, unique name of the error.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The message that describes the error.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// The PayPal internal ID. Used for correlation purposes.
    /// </summary>
    [JsonPropertyName("debug_id")]
    public required string DebugId { get; init; }

    /// <summary>
    /// The information link, or URI, that shows detailed information about this error for the developer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("information_link")]
    public string? InformationLink { get; init; }

    /// <summary>
    /// An array of additional details about the error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("details")]
    public IReadOnlyList<TransactionSearchErrorDetails>? Details { get; init; }

    /// <summary>
    /// An array of request-related <see href="/docs/api/reference/api-responses/#hateoas-links">HATEOAS links</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public IReadOnlyList<LinkDescription>? Links { get; init; }

    /// <summary>
    /// The total number of transactions. Valid only for <c>RESULTSET_TOO_LARGE</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_items")]
    [Minimum(0)]
    [Maximum(2147483647)]
    public int? TotalItems { get; init; }

    /// <summary>
    /// The maximum number of transactions. Valid only for <c>RESULTSET_TOO_LARGE</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maximum_items")]
    [Minimum(0)]
    [Maximum(2147483647)]
    public int? MaximumItems { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
