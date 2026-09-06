using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire shape of the <c>Create-Subscription-Request</c> schema.</summary>
public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Wire shape of the subset of <c>Create-Subscription</c> this integration uses: identify the
/// plan by handle, identify the already-ensured customer by id, and stamp our own reference so
/// the subscription can be looked up again by <c>findSubscription</c>.
/// </summary>
public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("product_price_point_handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// Collection-Method enum value. Omitted to fall back to the default of the site.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }
}
