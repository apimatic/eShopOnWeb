using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreditSchemeRequest
{
    [JsonPropertyName("credit_scheme")]
    public required CreditScheme CreditScheme { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
