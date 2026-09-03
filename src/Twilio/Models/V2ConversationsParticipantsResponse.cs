using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record V2ConversationsParticipantsResponse
{
    [JsonPropertyName("participants")]
    public required IReadOnlyList<ConversationsV2Participant> Participants { get; init; }

    [JsonPropertyName("meta")]
    public required Meta2 Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
