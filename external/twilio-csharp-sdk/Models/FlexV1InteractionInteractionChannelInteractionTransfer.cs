using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record FlexV1InteractionInteractionChannelInteractionTransfer
{
    /// <summary>
    /// The unique string created by Twilio to identify an Interaction Transfer resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UX[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the Instance associated with the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^GO[0-9a-fA-F]{32}$")]
    public string? InstanceSid { get; init; }

    /// <summary>
    /// The SID of the Account that created the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The Interaction Sid for this channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interaction_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KD[0-9a-fA-F]{32}$")]
    public string? InteractionSid { get; init; }

    /// <summary>
    /// The Channel Sid for this Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UO[0-9a-fA-F]{32}$")]
    public string? ChannelSid { get; init; }

    /// <summary>
    /// The Execution SID associated with the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("execution_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^QW[0-9a-fA-F]{32}$")]
    public string? ExecutionSid { get; init; }

    /// <summary>
    /// The type of the Transfer. Can be: <c>cold</c>, <c>warm</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public InteractionTransferEnumTransferType? Type { get; init; }

    /// <summary>
    /// The status of the Transfer. Can be: <c>active</c>, <c>completed</c>, <c>failed</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public InteractionTransferEnumTransferStatus? Status { get; init; }

    /// <summary>
    /// The SID of the Participant initiating the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UT[0-9a-fA-F]{32}$")]
    public string? From { get; init; }

    /// <summary>
    /// The SID of the Participant receiving the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>
    /// The SID of the Note associated with the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("note_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KQ[0-9a-fA-F]{32}$")]
    public string? NoteSid { get; init; }

    /// <summary>
    /// The SID of the Summary associated with the Transfer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("summary_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KQ[0-9a-fA-F]{32}$")]
    public string? SummarySid { get; init; }

    /// <summary>
    /// The date and time when the Transfer was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time when the Transfer was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
