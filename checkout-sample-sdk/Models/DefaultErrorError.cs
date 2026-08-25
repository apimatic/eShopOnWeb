using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The error details.
/// </summary>
public record DefaultErrorError
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
}
