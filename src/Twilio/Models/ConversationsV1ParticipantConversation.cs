using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ConversationsV1ParticipantConversation
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
    /// The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-participant-resource">Participant</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MB[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// The unique string that identifies the conversation participant as <see href="https://www.twilio.com/docs/conversations/api/user-resource">Conversation User</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_user_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^US[0-9a-fA-F]{32}$")]
    public string? ParticipantUserSid { get; init; }

    /// <summary>
    /// A unique string identifier for the conversation participant as <see href="https://www.twilio.com/docs/conversations/api/user-resource">Conversation User</see>. This parameter is non-null if (and only if) the participant is using the Conversations SDK to communicate. Limited to 256 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_identity")]
    public string? ParticipantIdentity { get; init; }

    /// <summary>
    /// Information about how this participant exchanges messages with the conversation. A JSON parameter consisting of type and address fields of the participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_messaging_binding")]
    public object? ParticipantMessagingBinding { get; init; }

    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation</see> this Participant belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CH[0-9a-fA-F]{32}$")]
    public string? ConversationSid { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the Conversation resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_unique_name")]
    public string? ConversationUniqueName { get; init; }

    /// <summary>
    /// The human-readable name of this conversation, limited to 256 characters. Optional.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_friendly_name")]
    public string? ConversationFriendlyName { get; init; }

    /// <summary>
    /// An optional string metadata field you can use to store any data you wish. The string value must contain structurally valid JSON if specified.  <b>Note</b> that if the attributes are not set "{}" will be returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_attributes")]
    public string? ConversationAttributes { get; init; }

    /// <summary>
    /// The date that this conversation was created, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_date_created")]
    public DateTimeOffset? ConversationDateCreated { get; init; }

    /// <summary>
    /// The date that this conversation was last updated, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_date_updated")]
    public DateTimeOffset? ConversationDateUpdated { get; init; }

    /// <summary>
    /// Identity of the creator of this Conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_created_by")]
    public string? ConversationCreatedBy { get; init; }

    /// <summary>
    /// The current state of this User Conversation. One of <c>inactive</c>, <c>active</c> or <c>closed</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_state")]
    public ParticipantConversationEnumState? ConversationState { get; init; }

    /// <summary>
    /// Timer date values representing state update for this conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_timers")]
    public object? ConversationTimers { get; init; }

    /// <summary>
    /// Contains absolute URLs to access the <see href="https://www.twilio.com/docs/conversations/api/conversation-participant-resource">participant</see> and <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">conversation</see> of this conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
