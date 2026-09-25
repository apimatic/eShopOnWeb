using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Models;

/// <summary>
/// The billing cycle details to override at subscription level. The subscription billing cycle definition has to adhere to the plan billing cycle definition.
/// </summary>
public record BillingCycleOverride
{
    /// <summary>
    /// The pricing scheme details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public SubscriptionPricingScheme? PricingScheme { get; init; }

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_cycles")]
    [Minimum(0)]
    [Maximum(999)]
    public int? TotalCycles { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
