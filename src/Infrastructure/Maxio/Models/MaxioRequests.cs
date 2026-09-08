using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// A customer to create in Maxio Advanced Billing. Sent as the <c>customer</c> payload to
/// <c>POST /customers.json</c>.
/// </summary>
public sealed class MaxioNewCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>
/// A subscription to create in Maxio Advanced Billing. Sent as the <c>subscription</c> payload
/// to <c>POST /subscriptions.json</c>.
/// </summary>
public sealed class MaxioNewSubscription
{
    /// <summary>The API handle of the product (plan) to subscribe to.</summary>
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>The id of an existing Maxio customer.</summary>
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// The payment collection method. <c>remittance</c> means no payment is attempted and no
    /// payment profile is required at signup; the subscription is billed by invoice.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } =
        Microsoft.eShopWeb.Infrastructure.Maxio.MaxioOptions.PaymentCollectionMethodRemittance;
}
