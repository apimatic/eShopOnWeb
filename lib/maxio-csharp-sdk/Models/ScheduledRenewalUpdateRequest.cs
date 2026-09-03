using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.OneOf;

namespace Maxio.Models;

public record ScheduledRenewalUpdateRequest
{
    [JsonPropertyName("renewal_configuration_item")]
    public required RenewalConfigurationItem RenewalConfigurationItem { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
