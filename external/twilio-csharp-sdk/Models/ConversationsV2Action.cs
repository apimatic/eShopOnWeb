using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ConversationsV2Action
{
    /// <summary>
    /// Unique identifier for this Action.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The type of action. Accepted values: SEND_MESSAGE.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Current status of the Action.
    /// - PENDING: Action accepted, awaiting downstream confirmation
    /// - COMPLETED: Downstream backend confirmed the action
    /// - FAILED: Downstream backend reported a failure
    /// </summary>
    [JsonPropertyName("status")]
    public required Status11 Status { get; init; }

    /// <summary>
    /// The conversation this action belongs to.
    /// </summary>
    [JsonPropertyName("conversationId")]
    public required string ConversationId { get; init; }

    /// <summary>
    /// Named identifiers from downstream. For SEND_MESSAGE:
    /// - messageSid: The downstream message SID (present when PENDING or COMPLETED)
    /// - communicationId: The Communication ID (present when COMPLETED)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("related")]
    public IReadOnlyDictionary<string, string>? Related { get; init; }

    /// <summary>
    /// Timestamp when the action was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the action was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Timestamp when the action reached a terminal status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
