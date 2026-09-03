using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ServiceCreditResponse
{
    [JsonPropertyName("service_credit")]
    public required ServiceCredit ServiceCredit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
