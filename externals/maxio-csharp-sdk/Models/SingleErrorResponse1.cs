using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SingleErrorResponse1
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
