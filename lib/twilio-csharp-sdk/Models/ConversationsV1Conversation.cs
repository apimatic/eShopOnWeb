using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ConversationsV1Conversation
{
    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> this conversation belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatServiceSid { get; init; }

    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> this conversation belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("messaging_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? MessagingServiceSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CH[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The human-readable name of this conversation, limited to 256 characters. Optional.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the resource. It can be used to address the resource in place of the resource's <c>sid</c> in the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// An optional string metadata field you can use to store any data you wish. The string value must contain structurally valid JSON if specified.  <b>Note</b> that if the attributes are not set "{}" will be returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public string? Attributes { get; init; }

    /// <summary>
    /// Current state of this conversation. Can be either <c>initializing</c>, <c>active</c>, <c>inactive</c> or <c>closed</c> and defaults to <c>active</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("state")]
    public ConversationEnumState? State { get; init; }

    /// <summary>
    /// The date that this resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// Timer date values representing state update for this conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timers")]
    public object? Timers { get; init; }

    /// <summary>
    /// An absolute API resource URL for this conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Contains absolute URLs to access the <see href="https://www.twilio.com/docs/conversations/api/conversation-participant-resource">participants</see>, <see href="https://www.twilio.com/docs/conversations/api/conversation-message-resource">messages</see> and <see href="https://www.twilio.com/docs/conversations/api/conversation-scoped-webhook-resource">webhooks</see> of this conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bindings")]
    public object? Bindings { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
