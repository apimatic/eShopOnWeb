using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Request payloads for the Maxio Advanced Billing REST API. Null members are omitted so that an
// unset value defers to the provider's default rather than overwriting it with null.

internal sealed class CreateCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateCustomerPayload Customer { get; set; } = new();
}

internal sealed class CreateCustomerPayload
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

internal sealed class CreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionPayload
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class UpdateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public UpdateSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class UpdateSubscriptionPayload
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>Defers the product change to the next renewal instead of prorating immediately.</summary>
    [JsonPropertyName("product_change_delayed")]
    public bool? ProductChangeDelayed { get; set; }
}

internal sealed class CancelSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CancelSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class CancelSubscriptionPayload
{
    [JsonPropertyName("cancellation_message")]
    public string? CancellationMessage { get; set; }
}

internal sealed class MigrationEnvelope
{
    [JsonPropertyName("migration")]
    public MigrationPayload Migration { get; set; } = new();
}

internal sealed class MigrationPayload
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
}

internal sealed class CreateUsageEnvelope
{
    [JsonPropertyName("usage")]
    public CreateUsagePayload Usage { get; set; } = new();
}

internal sealed class CreateUsagePayload
{
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }
}
