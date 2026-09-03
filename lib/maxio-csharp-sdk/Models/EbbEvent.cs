using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record EbbEvent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chargify")]
    public ChargifyEbb? Chargify { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
