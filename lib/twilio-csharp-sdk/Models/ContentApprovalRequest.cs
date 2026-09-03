using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Content approval request body
/// </summary>
public record ContentApprovalRequest
{
    /// <summary>
    /// Name of the template.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// A WhatsApp recognized template category.
    /// </summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
