using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Request body for <c>POST /subscriptions.json</c>.</summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// The subscription attributes eShopOnWeb sends on signup. The customer always already exists by this
/// point, so the customer is referenced by id rather than created inline.
/// </summary>
public class MaxioCreateSubscription
{
    /// <summary>Handle of the product to subscribe to. Handles are stable across re-seeds; numeric ids are not.</summary>
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>Reference assigned by this application. Maxio enforces uniqueness per site.</summary>
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    /// <summary>How Maxio collects payment, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
