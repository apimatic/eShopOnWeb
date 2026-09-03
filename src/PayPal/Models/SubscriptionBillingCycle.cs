using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The billing cycle details.
/// </summary>
public record SubscriptionBillingCycle
{
    /// <summary>
    /// The pricing scheme details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public SubscriptionPricingScheme? PricingScheme { get; init; }

    /// <summary>
    /// The frequency of the billing cycle.
    /// </summary>
    [JsonPropertyName("frequency")]
    public required Frequency Frequency { get; init; }

    /// <summary>
    /// The tenure type of the billing cycle. In case of a plan having trial cycle, only 2 trial cycles are allowed per plan.
    /// </summary>
    [JsonPropertyName("tenure_type")]
    public required TenureType TenureType { get; init; }

    /// <summary>
    /// The order in which this cycle is to run among other billing cycles. For example, a trial billing cycle has a <c>sequence</c> of <c>1</c> while a regular billing cycle has a <c>sequence</c> of <c>2</c>, so that trial cycle runs before the regular cycle.
    /// </summary>
    [JsonPropertyName("sequence")]
    [Minimum(1)]
    [Maximum(99)]
    public required int Sequence { get; init; }

    /// <summary>
    /// The number of times this billing cycle gets executed. Trial billing cycles can only be executed a finite number of times (value between 1 and 999 for total_cycles). Regular billing cycles can be executed infinite times (value of 0 for total_cycles) or a finite number of times (value between 1 and 999 for total_cycles).
    /// </summary>
    [JsonPropertyName("total_cycles")]
    [Minimum(0)]
    [Maximum(999)]
    public int? TotalCycles { get; init; } = 1;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
