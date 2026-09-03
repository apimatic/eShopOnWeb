using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ConversationsV1ConfigurationConfigurationWebhook
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
    /// The HTTP method to be used when sending a webhook request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("method")]
    public AmdStatusCallbackMethod? Method { get; init; }

    /// <summary>
    /// The list of webhook event triggers that are enabled for this Service: <c>onMessageAdded</c>, <c>onMessageUpdated</c>, <c>onMessageRemoved</c>, <c>onMessageAdd</c>, <c>onMessageUpdate</c>, <c>onMessageRemove</c>, <c>onConversationUpdated</c>, <c>onConversationRemoved</c>, <c>onConversationAdd</c>, <c>onConversationAdded</c>, <c>onConversationRemove</c>, <c>onConversationUpdate</c>, <c>onConversationStateUpdated</c>, <c>onParticipantAdded</c>, <c>onParticipantUpdated</c>, <c>onParticipantRemoved</c>, <c>onParticipantAdd</c>, <c>onParticipantRemove</c>, <c>onParticipantUpdate</c>, <c>onDeliveryUpdated</c>, <c>onUserAdded</c>, <c>onUserUpdate</c>, <c>onUserUpdated</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("filters")]
    public IReadOnlyList<string?>? Filters { get; init; }

    /// <summary>
    /// The absolute url the pre-event webhook request should be sent to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pre_webhook_url")]
    public string? PreWebhookUrl { get; init; }

    /// <summary>
    /// The absolute url the post-event webhook request should be sent to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("post_webhook_url")]
    public string? PostWebhookUrl { get; init; }

    /// <summary>
    /// The routing target of the webhook. Can be ordinary or route internally to Flex
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("target")]
    public ConfigurationWebhookEnumTarget? Target { get; init; }

    /// <summary>
    /// An absolute API resource API resource URL for this webhook.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
