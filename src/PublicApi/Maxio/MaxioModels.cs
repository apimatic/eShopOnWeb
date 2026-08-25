using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire-format models for the Maxio Advanced Billing API (https://{subdomain}.chargify.com).
// Only the fields this integration reads or writes are mapped; unknown fields are ignored.

public class MaxioProductFamilyWrapper
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class MaxioProductWrapper
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

public class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

public class MaxioCustomerAttributes
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

public class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionAttributes Subscription { get; set; } = new();
}

public class MaxioSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// "remittance" bills by invoice, so signup succeeds without capturing a payment method.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
