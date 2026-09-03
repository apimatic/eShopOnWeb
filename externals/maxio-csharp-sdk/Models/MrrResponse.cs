using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record MrrResponse
{
    [JsonPropertyName("mrr")]
    public required Mrr Mrr { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
