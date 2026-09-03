using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record FlexV1InsightsConversations
{
    /// <summary>
    /// The id of the account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    /// <summary>
    /// The unique id of the conversation
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; init; }

    /// <summary>
    /// The count of segments for a conversation
    /// </summary>
    [JsonPropertyName("segment_count")]
    public int? SegmentCount { get; init; } = 0;

    /// <summary>
    /// The Segments of a conversation
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("segments")]
    public IReadOnlyList<object?>? Segments { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
