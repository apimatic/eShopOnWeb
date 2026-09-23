using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

/// <summary>
/// Full configuration settings for this Conversation.
/// </summary>
public record ConfigurationModel
{
    /// <summary>
    /// A human-readable name for the configuration. Limited to 32 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("displayName")]
    [MaxLength(32)]
    [RegularExpression("^[a-zA-Z0-9-_ ]+$")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// Human-readable description for the Configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Type of Conversation grouping strategy:
    /// - <c>GROUP_BY_PROFILE</c>: Groups Communications by resolved Profile from the Memory Store.
    ///   A Profile is looked up or created for <c>CUSTOMER</c> Participant types. All Communications from the same Profile are in the same Conversation, regardless of address or channel.
    /// - <c>GROUP_BY_PARTICIPANT_ADDRESSES</c>: Groups Communications by Participant addresses across all channels.
    ///   A customer using +18005550100 will be in the same Conversation whether they contact by SMS, WhatsApp, or RCS.
    /// - <c>GROUP_BY_PARTICIPANT_ADDRESSES_AND_CHANNEL_TYPE</c>: Groups Communications by both Participant addresses AND channel.
    ///   A customer using +18005550100 by SMS will be in a different Conversation than the same customer by Voice.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversationGroupingType")]
    public ConversationGroupingType? ConversationGroupingType { get; init; }

    /// <summary>
    /// Memory Store ID for Profile resolution.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memoryStoreId")]
    public string? MemoryStoreId { get; init; }

    /// <summary>
    /// Channel-specific parameters forwarded as-is to the downstream sending service.
    /// Allows passing backend-specific fields without requiring API changes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelSettings")]
    public object? ChannelSettings { get; init; }

    /// <summary>
    /// List of default webhook configurations applied to Conversations under this Configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbacks")]
    [MaxLength(20)]
    public IReadOnlyList<ConversationsV2StatusCallbackConfig>? StatusCallbacks { get; init; }

    /// <summary>
    /// List of Intelligence Configuration IDs configured for this Configuration.
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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
