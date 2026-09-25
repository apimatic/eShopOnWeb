using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// An inline plan object to customise the subscription. You can override plan level default attributes by providing customised values for the subscription in this object.
/// </summary>
public record PlanOverride
{
    /// <summary>
    /// An array of billing cycles for trial billing and regular billing. The subscription billing cycle definition has to adhere to the plan billing cycle definition.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_cycles")]
    [MinLength(1)]
    [MaxLength(12)]
    public IReadOnlyList<BillingCycleOverride>? BillingCycles { get; init; }

    /// <summary>
    /// The payment preferences to override at subscription level.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_preferences")]
    public PaymentPreferencesOverride? PaymentPreferences { get; init; }

    /// <summary>
    /// The tax details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taxes")]
    public TaxesOverride? Taxes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
