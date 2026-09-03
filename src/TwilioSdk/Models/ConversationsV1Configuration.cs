using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record ConversationsV1Configuration
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> responsible for this configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the default <see href="https://www.twilio.com/docs/conversations/api/service-resource">Conversation Service</see> used when creating a conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_chat_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? DefaultChatServiceSid { get; init; }

    /// <summary>
    /// The SID of the default <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> used when creating a conversation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_messaging_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? DefaultMessagingServiceSid { get; init; }

    /// <summary>
    /// Default ISO8601 duration when conversation will be switched to <c>inactive</c> state. Minimum value for this timer is 1 minute.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_inactive_timer")]
    public string? DefaultInactiveTimer { get; init; }

    /// <summary>
    /// Default ISO8601 duration when conversation will be switched to <c>closed</c> state. Minimum value for this timer is 10 minutes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_closed_timer")]
    public string? DefaultClosedTimer { get; init; }

    /// <summary>
    /// An absolute API resource URL for this global configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Contains absolute API resource URLs to access the webhook and default service configurations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
