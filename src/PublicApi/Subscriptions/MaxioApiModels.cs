using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateMaxioCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

public sealed class MaxioCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int? PriceInCents { get; set; }

    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public string Handle { get; set; } = string.Empty;
}

internal sealed class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscription Subscription { get; set; } = new();
}

internal sealed class CreateMaxioSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTimeOffset? NextBillingAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int? ProductPriceInCents { get; set; }

    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioApiError
{
    public List<string> Errors { get; set; } = new();
}
