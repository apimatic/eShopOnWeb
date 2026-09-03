using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record StatusCallback1
{
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public required string Url { get; init; }

    [JsonPropertyName("method")]
    public Method21? Method { get; init; } = Method21.Post;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
