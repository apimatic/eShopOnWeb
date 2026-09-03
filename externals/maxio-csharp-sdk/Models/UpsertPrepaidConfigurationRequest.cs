using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record UpsertPrepaidConfigurationRequest
{
    [JsonPropertyName("prepaid_configuration")]
    public required UpsertPrepaidConfiguration PrepaidConfiguration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
