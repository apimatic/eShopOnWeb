using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Errors returned on creating a refund prepayment, grouped by field, as arrays of strings.
/// </summary>
public record RefundPrepaymentAggregatedErrorsResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public RefundPrepaymentAggregatedError? Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
