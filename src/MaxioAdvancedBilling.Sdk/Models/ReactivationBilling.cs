using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

/// <summary>
/// These values are only applicable to subscriptions using calendar billing.
/// </summary>
public record ReactivationBilling
{
    /// <summary>
    /// You may choose how to handle the reactivation charge for that subscription: 1) <c>prorated</c> A prorated charge for the product price will be attempted to complete the period 2) <c>immediate</c> A full-price charge for the product price will be attempted immediately 3) <c>delayed</c> A full-price charge for the product price will be attempted at the next renewal.
    /// </summary>
    [JsonPropertyName("reactivation_charge")]
    public ReactivationCharge? ReactivationCharge { get; init; } = ReactivationCharge.Prorated;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
