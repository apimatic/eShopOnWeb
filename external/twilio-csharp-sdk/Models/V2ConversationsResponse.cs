using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record V2ConversationsResponse
{
    [JsonPropertyName("conversations")]
    [MinLength(0)]
    public required IReadOnlyList<ConversationsV2Conversation> Conversations { get; init; }

    [JsonPropertyName("meta")]
    public required Meta1 Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
