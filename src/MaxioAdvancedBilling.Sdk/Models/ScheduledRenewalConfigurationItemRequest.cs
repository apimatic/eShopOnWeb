using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Models;

public record ScheduledRenewalConfigurationItemRequest
{
    [JsonPropertyName("renewal_configuration_item")]
    public required RenewalConfigurationItem RenewalConfigurationItem { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
