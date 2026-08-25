using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Validation.Attributes;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The billing cycle providing details of the billing frequency, amount, duration and if the billing cycle is a free, discounted or regular billing cycle. The sequence of the billing cycle will be in the following order - free trial billing cycle(s), discounted trial billing cycle(s), regular billing cycle(s).
/// </summary>
public record BillingCycle
{
    /// <summary>
    /// The tenure type of the billing cycle identifies if the billing cycle is a trial(free or discounted) or regular billing cycle.
    /// </summary>
    [JsonPropertyName("tenure_type")]
    public required TenureType TenureType { get; init; }

    /// <summary>
    /// The pricing scheme details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public PricingScheme? PricingScheme { get; init; }

    /// <summary>
    /// The number of times this billing cycle gets executed. Trial billing cycles can only be executed a finite number of times (value between 1 and 999 for total_cycles). Regular billing cycles can be executed infinite times (value of 0 for total_cycles) or a finite number of times (value between 1 and 999 for total_cycles).
    /// </summary>
    [JsonPropertyName("total_cycles")]
    [Minimum(0)]
    [Maximum(999)]
    public int? TotalCycles { get; init; } = 1;

    /// <summary>
    /// The order in which this cycle is to run among other billing cycles. For example, a trial billing cycle has a <c>sequence</c> of <c>1</c> while a regular billing cycle has a <c>sequence</c> of <c>2</c>, so that trial cycle runs before the regular cycle.
    /// </summary>
    [JsonPropertyName("sequence")]
    [Minimum(1)]
    [Maximum(3)]
    public int? Sequence { get; init; } = 1;

    /// <summary>
    /// The stand-alone date, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. To represent special legal values, such as a date of birth, you should use dates with no associated time or time-zone data. Whenever possible, use the standard <c>date_time</c> type. This regular expression does not validate all dates. For example, February 31 is valid and nothing is known about leap years.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_date")]
    [StringLength(10, MinimumLength = 10)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])$")]
    public string? StartDate { get; init; }
}
