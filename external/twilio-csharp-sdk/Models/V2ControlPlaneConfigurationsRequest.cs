using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record V2ControlPlaneConfigurationsRequest
{
    /// <summary>
    /// A human-readable name for the configuration. Limited to 32 characters.
    /// </summary>
    [JsonPropertyName("displayName")]
    [MaxLength(32)]
    [RegularExpression("^[a-zA-Z0-9-_ ]+$")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Human-readable description for the configuration.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// The strategy Conversation Orchestrator uses to assign communications to conversations.
    /// </summary>
    [JsonPropertyName("conversationGroupingType")]
    public required ConversationGroupingType3 ConversationGroupingType { get; init; }

    /// <summary>
    /// The memory store ID that Conversation Orchestrator uses for profile resolution.
    /// </summary>
    [JsonPropertyName("memoryStoreId")]
    public required string MemoryStoreId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channelSettings")]
    public IReadOnlyDictionary<string, ChannelSettings>? ChannelSettings { get; init; }

    /// <summary>
    /// A list of webhook configurations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbacks")]
    [MaxLength(2)]
    public IReadOnlyList<StatusCallback>? StatusCallbacks { get; init; }

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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
