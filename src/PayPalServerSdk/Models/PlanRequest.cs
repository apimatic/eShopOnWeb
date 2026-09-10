using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The create plan request details.
/// </summary>
public record PlanRequest
{
    /// <summary>
    /// The ID of the product created through Catalog Products API.
    /// </summary>
    [JsonPropertyName("product_id")]
    [StringLength(22, MinimumLength = 22)]
    [RegularExpression("^PROD-[A-Z0-9]*$")]
    public required string ProductId { get; init; }

    /// <summary>
    /// The plan name.
    /// </summary>
    [JsonPropertyName("name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public required string Name { get; init; }

    /// <summary>
    /// The initial state of the plan. Allowed input values are CREATED and ACTIVE.
    /// </summary>
    [JsonPropertyName("status")]
    public PlanRequestStatus? Status { get; init; } = PlanRequestStatus.Active;

    /// <summary>
    /// The detailed description of the plan.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? Description { get; init; }

    /// <summary>
    /// An array of billing cycles for trial billing and regular billing. A plan can have at most two trial cycles and only one regular cycle.
    /// </summary>
    [JsonPropertyName("billing_cycles")]
    [MinLength(1)]
    [MaxLength(12)]
    public required IReadOnlyList<SubscriptionBillingCycle> BillingCycles { get; init; }

    /// <summary>
    /// The payment preferences for a subscription.
    /// </summary>
    [JsonPropertyName("payment_preferences")]
    public required PaymentPreferences PaymentPreferences { get; init; }

    /// <summary>
    /// The merchant preferences for a subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("merchant_preferences")]
    public MerchantPreferences? MerchantPreferences { get; init; }

    /// <summary>
    /// The tax details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taxes")]
    public Taxes? Taxes { get; init; }

    /// <summary>
    /// Indicates whether you can subscribe to this plan by providing a quantity for the goods or service.
    /// </summary>
    [JsonPropertyName("quantity_supported")]
    public bool? QuantitySupported { get; init; } = false;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
