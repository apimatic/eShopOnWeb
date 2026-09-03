using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// The configuration settings for webhooks.
/// </summary>
public record MessagingV2ChannelsSenderWebhook
{
    /// <summary>
    /// The URL to send the webhook to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("callback_url")]
    public string? CallbackUrl { get; init; }

    /// <summary>
    /// The HTTP method for the webhook.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("callback_method")]
    public CallbackMethod2? CallbackMethod { get; init; }

    /// <summary>
    /// The URL to send the fallback webhook to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fallback_url")]
    public string? FallbackUrl { get; init; }

    /// <summary>
    /// The HTTP method for the fallback webhook.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fallback_method")]
    public FallbackMethod1? FallbackMethod { get; init; }

    /// <summary>
    /// The URL to send the status callback to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback_url")]
    public string? StatusCallbackUrl { get; init; }

    /// <summary>
    /// The HTTP method for the status callback.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback_method")]
    public string? StatusCallbackMethod { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
