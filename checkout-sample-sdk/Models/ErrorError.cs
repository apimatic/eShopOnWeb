using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The error details.
/// </summary>
public record ErrorError
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
    /// An array of additional details about the error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("details")]
    public IReadOnlyList<ErrorDetails>? Details { get; init; }

    /// <summary>
    /// An array of request-related <see href="/api/rest/responses/#hateoas-links">HATEOAS links</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public IReadOnlyList<LinkDescription>? Links { get; init; }
}
