using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record IssueServiceCreditRequest
{
    [JsonPropertyName("service_credit")]
    public required IssueServiceCredit ServiceCredit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
