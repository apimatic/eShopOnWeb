using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio OpenAPI schema <c>Create-Subscription</c> (components/schemas/Create-Subscription.yaml).
/// Only the properties this integration sends are modelled; null properties are omitted from the
/// payload so Maxio applies its own defaults.
/// </summary>
public class CreateSubscription
{
    /// <summary>
    /// API handle of the plan. The schema notes the product id "is not currently published, so we
    /// recommend using the API Handle instead" — and handles survive catalog re-seeds.
    /// </summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>Reference of an existing customer, used instead of <c>customer_attributes</c> so the
    /// customer is never created twice.</summary>
    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    /// <summary>Id of an existing customer. Preferred over the reference once it is known.</summary>
    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    /// <summary>Schema <c>Collection-Method</c>.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>This application's reference for the subscription itself; deterministic per
    /// (customer, plan) so duplicates are recognisable in Maxio.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>Maxio OpenAPI schema <c>Create-Subscription-Request</c> (components/schemas/Create-Subscription-Request.yaml).</summary>
public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscription Subscription { get; set; } = new();
}
