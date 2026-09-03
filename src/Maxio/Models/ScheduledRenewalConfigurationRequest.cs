using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ScheduledRenewalConfigurationRequest
{
    [JsonPropertyName("renewal_configuration")]
    public required ScheduledRenewalConfigurationRequestBody RenewalConfiguration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
