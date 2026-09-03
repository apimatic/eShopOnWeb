using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record OfferResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offer")]
    public Offer? Offer { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
