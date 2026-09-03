using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record DeductServiceCreditRequest
{
    [JsonPropertyName("deduction")]
    public required DeductServiceCredit Deduction { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
