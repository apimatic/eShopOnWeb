using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record ScheduledRenewalConfigurationRequest
{
    [JsonPropertyName("renewal_configuration")]
    public required ScheduledRenewalConfigurationRequestBody RenewalConfiguration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
