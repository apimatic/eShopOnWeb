using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ProformaBadRequestErrorResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public ProformaError? Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
