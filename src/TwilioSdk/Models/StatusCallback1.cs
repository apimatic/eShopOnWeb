using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

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
