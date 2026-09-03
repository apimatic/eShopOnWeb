using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record Author
{
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    [JsonPropertyName("channel")]
    public required Channel6 Channel { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
