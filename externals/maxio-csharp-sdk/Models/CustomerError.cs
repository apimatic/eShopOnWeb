using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CustomerError
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public string? Customer { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
