using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The regular and trial execution details for a billing cycle.
/// </summary>
public record CycleExecution
{
    /// <summary>
    /// The type of the billing cycle.
    /// </summary>
    [JsonPropertyName("tenure_type")]
    public required TenureType TenureType { get; init; }

    /// <summary>
    /// The order in which to run this cycle among other billing cycles.
    /// </summary>
    [JsonPropertyName("sequence")]
    [Minimum(0)]
    [Maximum(99)]
    public required int Sequence { get; init; }

    /// <summary>
    /// The number of billing cycles that have completed.
    /// </summary>
    [JsonPropertyName("cycles_completed")]
    [Minimum(0)]
    [Maximum(9999)]
    public required int CyclesCompleted { get; init; }

    /// <summary>
    /// For a finite billing cycle, cycles_remaining is the number of remaining cycles. For an infinite billing cycle, cycles_remaining is set as 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cycles_remaining")]
    [Minimum(0)]
    [Maximum(9999)]
    public int? CyclesRemaining { get; init; }

    /// <summary>
    /// The active pricing scheme version for the billing cycle.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_pricing_scheme_version")]
    [Minimum(1)]
    [Maximum(99)]
    public int? CurrentPricingSchemeVersion { get; init; }

    /// <summary>
    /// The number of times this billing cycle gets executed. Trial billing cycles can only be executed a finite number of times (value between 1 and 999 for total_cycles). Regular billing cycles can be executed infinite times (value of 0 for total_cycles) or a finite number of times (value between 1 and 999 for total_cycles).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_cycles")]
    [Minimum(0)]
    [Maximum(999)]
    public int? TotalCycles { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
