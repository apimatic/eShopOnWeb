using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Updatable fields for Subscription Note
/// </summary>
public record UpdateSubscriptionNoteRequest
{
    /// <summary>
    /// Updatable fields for Subscription Note
    /// </summary>
    [JsonPropertyName("note")]
    public required UpdateSubscriptionNote Note { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
