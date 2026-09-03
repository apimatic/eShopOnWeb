using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record FlexV1InteractionInteractionChannel
{
    /// <summary>
    /// The unique string created by Twilio to identify an Interaction Channel resource, prefixed with UO.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UO[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The unique string created by Twilio to identify an Interaction resource, prefixed with KD.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interaction_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KD[0-9a-fA-F]{32}$")]
    public string? InteractionSid { get; init; }

    /// <summary>
    /// The Interaction Channel's type. Can be: <c>sms</c>, <c>email</c>, <c>chat</c>, <c>whatsapp</c>, <c>web</c>, <c>messenger</c>, or <c>gbm</c>.
    ///  <b>Note:</b> These can be different from the task channel type specified in the Routing attributes. Task channel type corresponds to channel capacity while this channel type is the actual media type
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public InteractionChannelEnumType? Type { get; init; }

    /// <summary>
    /// The status of this channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public InteractionChannelEnumChannelStatus? Status { get; init; }

    /// <summary>
    /// The Twilio error code for a failed channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>
    /// The error message for a failed channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
