using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record PrepaidConfigurationResponse
{
    [JsonPropertyName("prepaid_configuration")]
    public required PrepaidConfiguration PrepaidConfiguration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
