using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record V2ConversationsCommunicationsResponse
{
    [JsonPropertyName("communications")]
    public required IReadOnlyList<ConversationsV2Communication> Communications { get; init; }

    [JsonPropertyName("meta")]
    public required Meta2 Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
