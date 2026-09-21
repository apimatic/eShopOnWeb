using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

/// <summary>
/// Configuration for Conversations.
/// </summary>
public record ConversationsV2Configuration
{
    /// <summary>
    /// Configuration ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// A human-readable name for the configuration. Limited to 32 characters.
    /// </summary>
    [JsonPropertyName("displayName")]
    [MaxLength(32)]
    [RegularExpression("^[a-zA-Z0-9-_ ]+$")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Human-readable description for the Configuration. Allows spaces and special characters, typically limited to a paragraph of text. This serves as a descriptive field rather than just a name.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Type of Conversation grouping strategy:
    /// - <c>GROUP_BY_PROFILE</c>: Groups Communications by resolved Profile from the Memory Store.
    ///   A Profile is looked up or created for <c>CUSTOMER</c> Participant types. All Communications from the same Profile are in the same Conversation, regardless of address or channel.
    /// - <c>GROUP_BY_PARTICIPANT_ADDRESSES</c>: Groups Communications by Participant addresses across all channels.
    ///   A customer using +18005550100 will be in the same Conversation whether they contact by SMS, WhatsApp, or RCS.
    /// - <c>GROUP_BY_PARTICIPANT_ADDRESSES_AND_CHANNEL_TYPE</c>: Groups Communications by both Participant addresses AND channel.
    ///   A customer using +18005550100 by SMS will be in a different Conversation than the same customer by Voice.
    /// </summary>
    [JsonPropertyName("conversationGroupingType")]
    public required ConversationGroupingType ConversationGroupingType { get; init; }

    /// <summary>
    /// Memory Store ID for Profile resolution.
    /// </summary>
    [JsonPropertyName("memoryStoreId")]
    public required string MemoryStoreId { get; init; }

    /// <summary>
    /// Channel-specific configuration settings by channel type. Keys should be valid channel types (<c>VOICE</c>, <c>SMS</c>, <c>RCS</c>, <c>WHATSAPP</c>, <c>CHAT</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelSettings")]
    public IReadOnlyDictionary<string, ConversationsV2ChannelSetting>? ChannelSettings { get; init; }

    /// <summary>
    /// List of default webhook configurations applied to Conversations under this Configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbacks")]
    [MaxLength(20)]
    public IReadOnlyList<ConversationsV2StatusCallbackConfig>? StatusCallbacks { get; init; }

    /// <summary>
    /// A list of Conversational Intelligence configuration IDs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("intelligenceConfigurationIds")]
    [MaxLength(5)]
    public IReadOnlyList<string>? IntelligenceConfigurationIds { get; init; }

    /// <summary>
    /// Whether memory extraction is enabled for conversations under this configuration. Defaults to false.
    /// </summary>
    [JsonPropertyName("memoryExtractionEnabled")]
    public bool? MemoryExtractionEnabled { get; init; } = false;

    /// <summary>
    /// Configuration for Conversations V1 bridge. When set, messaging channels route through Conversations V1. Use this to integrate with existing Conversations V1 applications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversationsV1Bridge")]
    public ConversationsV2ConversationsV1Bridge? ConversationsV1Bridge { get; init; }

    /// <summary>
    /// Timestamp when this Configuration was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when this Configuration was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Version number used for optimistic locking.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("version")]
    public long? Version { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
