using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListMrrResponse
{
    [JsonPropertyName("mrr")]
    public required ListMrrResponseResult Mrr { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
