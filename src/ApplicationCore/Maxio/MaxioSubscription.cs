using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Payload for creating a new Maxio subscription against an existing customer (identified by reference).
/// </summary>
public class NewMaxioSubscription
{
    public NewMaxioSubscription(string productHandle, string customerReference)
    {
        ProductHandle = productHandle;
        CustomerReference = customerReference;
    }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; }

    [JsonPropertyName("customer_reference")]
    public string CustomerReference { get; }

    // The seeded plans collect no payment method at signup, so billing must be collected via
    // invoice rather than an automatic card charge (which Maxio would otherwise attempt - and
    // reject for lack of a payment profile - for any non-zero amount due at signup).
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; } = "invoice";
}

public class NewMaxioSubscriptionEnvelope
{
    public NewMaxioSubscriptionEnvelope(NewMaxioSubscription subscription)
    {
        Subscription = subscription;
    }

    [JsonPropertyName("subscription")]
    public NewMaxioSubscription Subscription { get; }
}
