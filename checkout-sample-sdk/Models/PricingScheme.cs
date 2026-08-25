using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The pricing scheme details.
/// </summary>
public record PricingScheme
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public Money? Price { get; init; }

    /// <summary>
    /// The pricing model for the billing cycle.
    /// </summary>
    [JsonPropertyName("pricing_model")]
    public required PricingModel PricingModel { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reload_threshold_amount")]
    public Money? ReloadThresholdAmount { get; init; }
}
