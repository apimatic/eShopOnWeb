using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Partial configuration update for an existing conversation. Only statusCallbacks can be modified.
/// </summary>
public record ConversationsV2PatchConversationConfiguration
{
    /// <summary>
    /// List of webhook configurations for this conversation. Send an empty array to clear all callbacks and stop webhook delivery.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbacks")]
    [MaxLength(20)]
    public IReadOnlyList<ConversationsV2StatusCallbackConfig>? StatusCallbacks { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
