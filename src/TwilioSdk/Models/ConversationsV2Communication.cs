using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.AnyOf;

namespace TwilioSdk.Models;

public record ConversationsV2Communication
{
    /// <summary>
    /// Communication ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Conversation ID.
    /// </summary>
    [JsonPropertyName("conversationId")]
    public required string ConversationId { get; init; }

    /// <summary>
    /// Account ID.
    /// </summary>
    [JsonPropertyName("accountId")]
    public required string AccountId { get; init; }

    [JsonPropertyName("author")]
    public required ConversationsV2ParticipantAddress Author { get; init; }

    /// <summary>
    /// The content of the Communication using type field for discrimination.
    /// </summary>
    [JsonPropertyName("content")]
    public required Content Content { get; init; }

    /// <summary>
    /// Channel-specific reference ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelId")]
    public string? ChannelId { get; init; }

    /// <summary>
    /// External resource identifier for this Communication (e.g. MessageSid for SMS/RCS/WhatsApp, TranscriptionSid + MessageIndex for Voice). When set, used for Communication deduplication/uniqueness within a Conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resourceId")]
    [MinLength(1)]
    public string? ResourceId { get; init; }

    /// <summary>
    /// Communication recipients.
    /// </summary>
    [JsonPropertyName("recipients")]
    public required IReadOnlyList<Recipient> Recipients { get; init; }

    /// <summary>
    /// Timestamp when this Communication was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when this Communication was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// ISO 8601 timestamp when the communication occurred.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset? OccurredAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
