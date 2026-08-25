using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Metadata for merchant-managed recurring billing plans. Valid only during the saved payment method token or billing agreement creation.
/// </summary>
public record OrderBillingPlan
{
    /// <summary>
    /// An array of billing cycles for trial billing and regular billing. A plan can have at most two trial cycles and only one regular cycle.
    /// </summary>
    [JsonPropertyName("billing_cycles")]
    [MinLength(1)]
    [MaxLength(3)]
    public required IReadOnlyList<BillingCycle> BillingCycles { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("setup_fee")]
    public Money? SetupFee { get; init; }

    /// <summary>
    /// Name of the recurring plan.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9() +',.:-]+$")]
    public string? Name { get; init; }
}
