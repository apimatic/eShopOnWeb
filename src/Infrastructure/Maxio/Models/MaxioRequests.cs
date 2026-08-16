using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Request wire models. Only the fields eShopOnWeb needs are included; all are optional on the
// wire so the serializer omits nulls (WhenWritingNull) and Maxio applies its own defaults.

/// <summary>Body for POST /customers.json. Mirrors Create-Customer-Request.yaml.</summary>
internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerBody Customer { get; set; } = new();
}

/// <summary>Mirrors Create-Customer.yaml (subset). first_name/last_name/email are required by Maxio.</summary>
internal sealed class CreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>Body for POST /subscriptions.json. Mirrors Create-Subscription-Request.yaml.</summary>
internal sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

/// <summary>Mirrors Create-Subscription.yaml (subset).</summary>
internal sealed class CreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
