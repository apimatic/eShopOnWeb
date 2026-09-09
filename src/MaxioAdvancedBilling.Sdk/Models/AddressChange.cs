using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record AddressChange
{
    [JsonPropertyName("before")]
    public required InvoiceAddress Before { get; init; }

    [JsonPropertyName("after")]
    public required InvoiceAddress After { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
