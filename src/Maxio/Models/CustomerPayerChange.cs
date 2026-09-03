using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CustomerPayerChange
{
    [JsonPropertyName("before")]
    public required InvoicePayerChange Before { get; init; }

    [JsonPropertyName("after")]
    public required InvoicePayerChange After { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
