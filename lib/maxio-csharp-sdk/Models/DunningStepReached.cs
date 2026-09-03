using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record DunningStepReached
{
    [JsonPropertyName("dunner")]
    public required DunnerData Dunner { get; init; }

    [JsonPropertyName("current_step")]
    public required DunningStepData CurrentStep { get; init; }

    [JsonPropertyName("next_step")]
    public required DunningStepData NextStep { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
