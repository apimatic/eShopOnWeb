using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record UpsertPrepaidConfigurationRequest
{
    [JsonPropertyName("prepaid_configuration")]
    public required UpsertPrepaidConfiguration PrepaidConfiguration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
