using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ProxyV1ServiceSessionParticipantMessageInteraction
{
    /// <summary>
    /// The unique string that we created to identify the MessageInteraction resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KI[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/session">Session</see> resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("session_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KC[0-9a-fA-F]{32}$")]
    public string? SessionSid { get; init; }

    /// <summary>
    /// The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KS[0-9a-fA-F]{32}$")]
    public string? ServiceSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the MessageInteraction resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// A JSON string that includes the message body sent to the participant. (e.g. <c>{"body": "hello"}</c>)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>
    /// The Type of Message Interaction. This value is always <c>message</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public MessageInteractionEnumType? Type { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/proxy/api/participant">Participant</see> resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KP[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// Always empty for created Message Interactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KP[0-9a-fA-F]{32}$")]
    public string? InboundParticipantSid { get; init; }

    /// <summary>
    /// Always empty for created Message Interactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_resource_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^[a-zA-Z]{2}[0-9a-fA-F]{32}$")]
    public string? InboundResourceSid { get; init; }

    /// <summary>
    /// Always empty for created Message Interactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_resource_status")]
    public MessageInteractionEnumResourceStatus? InboundResourceStatus { get; init; }

    /// <summary>
    /// Always empty for created Message Interactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_resource_type")]
    public string? InboundResourceType { get; init; }

    /// <summary>
    /// Always empty for created Message Interactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_resource_url")]
    [Format(FormatKind.Uri)]
    public string? InboundResourceUrl { get; init; }

    /// <summary>
    /// The SID of the outbound <see href="https://www.twilio.com/docs/proxy/api/participant">Participant</see> resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KP[0-9a-fA-F]{32}$")]
    public string? OutboundParticipantSid { get; init; }

    /// <summary>
    /// The SID of the outbound <see href="https://www.twilio.com/docs/sms/api/message-resource">Message</see> resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_resource_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^[a-zA-Z]{2}[0-9a-fA-F]{32}$")]
    public string? OutboundResourceSid { get; init; }

    /// <summary>
    /// Always empty for created Message Interactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_resource_status")]
    public MessageInteractionEnumResourceStatus? OutboundResourceStatus { get; init; }

    /// <summary>
    /// The outbound resource type. This value is always <c>Message</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_resource_type")]
    public string? OutboundResourceType { get; init; }

    /// <summary>
    /// The URL of the Twilio message resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_resource_url")]
    [Format(FormatKind.Uri)]
    public string? OutboundResourceUrl { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the resource was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The absolute URL of the MessageInteraction resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
