using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The merchant level Recurring Billing plan metadata for the Billing Agreement.
/// </summary>
public record Plan
{
    /// <summary>
    /// An array of billing cycles for trial billing and regular billing. A plan can have at most two trial cycles and only one regular cycle.
    /// </summary>
    [JsonPropertyName("billing_cycles")]
    [MinLength(1)]
    [MaxLength(3)]
    public required IReadOnlyList<BillingCycle> BillingCycles { get; init; }

    /// <summary>
    /// The one-time charge info at the time of checkout.
    /// </summary>
    [JsonPropertyName("one_time_charges")]
    public required OneTimeCharge OneTimeCharges { get; init; }

    /// <summary>
    /// Name of the recurring plan.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9() +',.:-]+$")]
    public string? Name { get; init; }
}
