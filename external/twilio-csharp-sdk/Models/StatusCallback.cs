using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record StatusCallback
{
    /// <summary>
    /// The destination URL for webhooks.
    /// </summary>
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    /// <summary>
    /// The HTTP method used to invoke the webhook URL.
    /// </summary>
    [JsonPropertyName("method")]
    public Method11? Method { get; init; } = Method11.Post;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
