using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record IssueServiceCreditRequest
{
    [JsonPropertyName("service_credit")]
    public required IssueServiceCredit ServiceCredit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
