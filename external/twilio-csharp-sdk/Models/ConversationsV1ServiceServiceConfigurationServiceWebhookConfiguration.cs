using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ConversationsV1ServiceServiceConfigurationServiceWebhookConfiguration
{
    /// <summary>
    /// The unique ID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this service.
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
    /// The absolute url the pre-event webhook request should be sent to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pre_webhook_url")]
    [Format(FormatKind.Uri)]
    public string? PreWebhookUrl { get; init; }

    /// <summary>
    /// The absolute url the post-event webhook request should be sent to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("post_webhook_url")]
    [Format(FormatKind.Uri)]
    public string? PostWebhookUrl { get; init; }

    /// <summary>
    /// The list of events that your configured webhook targets will receive. Events not configured here will not fire. Possible values are <c>onParticipantAdd</c>, <c>onParticipantAdded</c>, <c>onDeliveryUpdated</c>, <c>onConversationUpdated</c>, <c>onConversationRemove</c>, <c>onParticipantRemove</c>, <c>onConversationUpdate</c>, <c>onMessageAdd</c>, <c>onMessageRemoved</c>, <c>onParticipantUpdated</c>, <c>onConversationAdded</c>, <c>onMessageAdded</c>, <c>onConversationAdd</c>, <c>onConversationRemoved</c>, <c>onParticipantUpdate</c>, <c>onMessageRemove</c>, <c>onMessageUpdated</c>, <c>onParticipantRemoved</c>, <c>onMessageUpdate</c> or <c>onConversationStateUpdated</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("filters")]
    public IReadOnlyList<string?>? Filters { get; init; }

    /// <summary>
    /// The HTTP method to be used when sending a webhook request. One of <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("method")]
    public AmdStatusCallbackMethod? Method { get; init; }

    /// <summary>
    /// An absolute API resource URL for this webhook.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
