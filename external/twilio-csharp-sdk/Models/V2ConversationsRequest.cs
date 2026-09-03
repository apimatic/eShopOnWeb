using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record V2ConversationsRequest
{
    /// <summary>
    /// The ID of an existing configuration.
    /// </summary>
    [JsonPropertyName("configurationId")]
    public required string ConfigurationId { get; init; }

    /// <summary>
    /// The name of the conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Conversation configuration settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("configuration")]
    public Configuration3? Configuration { get; init; }

    /// <summary>
    /// Optional list of Participants to create with the Conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participants")]
    [MaxLength(50)]
    public IReadOnlyList<Participant>? Participants { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
