using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ProductFamilyResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_family")]
    public ProductFamily? ProductFamily { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
