using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record ConversationsV1ConversationConversationMessage
{
    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> for this message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CH[0-9a-fA-F]{32}$")]
    public string? ConversationSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IM[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The index of the message within the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see>.  Indices may skip numbers, but will always be in order of when the message was received.
    /// </summary>
    [JsonPropertyName("index")]
    public int? Index { get; init; } = 0;

    /// <summary>
    /// The channel specific identifier of the message's author. Defaults to <c>system</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>
    /// The content of the message, can be up to 1,600 characters long.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>
    /// An array of objects that describe the Message's media, if the message contains media. Each object contains these fields: <c>content_type</c> with the MIME type of the media, <c>filename</c> with the name of the media, <c>sid</c> with the SID of the Media resource, and <c>size</c> with the media object's file size in bytes. If the Message has no media, this value is <c>null</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media")]
    public IReadOnlyList<object?>? Media { get; init; }

    /// <summary>
    /// A string metadata field you can use to store any data you wish. The string value must contain structurally valid JSON if specified.  <b>Note</b> that if the attributes are not set "{}" will be returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public string? Attributes { get; init; }

    /// <summary>
    /// The unique ID of messages's author participant. Null in case of <c>system</c> sent message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MB[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// The date that this resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was last updated. <c>null</c> if the message has not been edited.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// An absolute API resource API URL for this message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// An object that contains the summary of delivery statuses for the message to non-chat participants.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("delivery")]
    public object? Delivery { get; init; }

    /// <summary>
    /// Contains an absolute API resource URL to access the delivery &amp; read receipts of this message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    /// <summary>
    /// The unique ID of the multi-channel <see href="https://www.twilio.com/docs/content">Rich Content</see> template.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("content_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HX[0-9a-fA-F]{32}$")]
    public string? ContentSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
