using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SingleErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
