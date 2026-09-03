using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ServiceConversationMessageReceipt
{
    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> the Message resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatServiceSid { get; init; }

    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CH[0-9a-fA-F]{32}$")]
    public string? ConversationSid { get; init; }

    /// <summary>
    /// The SID of the message within a <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> the delivery receipt belongs to
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IM[0-9a-fA-F]{32}$")]
    public string? MessageSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^DY[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// A messaging channel-specific identifier for the message delivered to participant e.g. <c>SMxx</c> for SMS, <c>WAxx</c> for Whatsapp etc.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel_message_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^[a-zA-Z]{2}[0-9a-fA-F]{32}$")]
    public string? ChannelMessageSid { get; init; }

    /// <summary>
    /// The unique ID of the participant the delivery receipt belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MB[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// The message delivery status, can be <c>read</c>, <c>failed</c>, <c>delivered</c>, <c>undelivered</c>, <c>sent</c> or null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public ServiceConversationMessageReceiptEnumDeliveryStatus? Status { get; init; }

    /// <summary>
    /// The message <see href="https://www.twilio.com/docs/sms/api/message-resource#delivery-related-errors">delivery error code</see> for a <c>failed</c> status,
    /// </summary>
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; } = 0;

    /// <summary>
    /// The date that this resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was last updated. <c>null</c> if the delivery receipt has not been updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// An absolute API resource URL for this delivery receipt.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
