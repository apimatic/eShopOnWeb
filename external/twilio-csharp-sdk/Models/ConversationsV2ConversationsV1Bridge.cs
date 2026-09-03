using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Configuration for Conversations V1 bridge. When set, messaging channels route through Conversations V1. Use this to integrate with existing Conversations V1 applications.
/// </summary>
public record ConversationsV2ConversationsV1Bridge
{
    /// <summary>
    /// The Conversations V1 Service SID (IS prefix). One configuration per V1 Service SID.
    /// </summary>
    [JsonPropertyName("serviceId")]
    [RegularExpression("^IS[0-9a-f]{32}$")]
    public required string ServiceId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
