using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-level models mirroring the shapes documented in maxio-spec/components/schemas.
// Only the fields this integration actually consumes are declared; System.Text.Json ignores
// the rest of the payload.

internal sealed class MaxioProductWrapper
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
}

internal sealed class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// "remittance" collects no automatic card payment, which is what plans configured with
    /// "payment method not required" (require_credit_card: false) expect at signup.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
