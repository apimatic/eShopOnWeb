using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record DeductServiceCreditRequest
{
    [JsonPropertyName("deduction")]
    public required DeductServiceCredit Deduction { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
