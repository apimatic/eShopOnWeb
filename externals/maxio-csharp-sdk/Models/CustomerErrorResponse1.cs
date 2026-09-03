using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;

namespace Maxio.Models;

public record CustomerErrorResponse1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public Errors1? Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
