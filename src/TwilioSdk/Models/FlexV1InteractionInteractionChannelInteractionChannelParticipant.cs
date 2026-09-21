using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record FlexV1InteractionInteractionChannelInteractionChannelParticipant
{
    /// <summary>
    /// The unique string created by Twilio to identify an Interaction Channel Participant resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UT[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// Participant type. Can be: <c>agent</c>, <c>customer</c>, <c>supervisor</c>, <c>external</c>, <c>unknown</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public InteractionChannelParticipantEnumType? Type { get; init; }

    /// <summary>
    /// The Interaction Sid for this channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interaction_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KD[0-9a-fA-F]{32}$")]
    public string? InteractionSid { get; init; }

    /// <summary>
    /// The Channel Sid for this Participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UO[0-9a-fA-F]{32}$")]
    public string? ChannelSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The Participant's routing properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("routing_properties")]
    public object? RoutingProperties { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
