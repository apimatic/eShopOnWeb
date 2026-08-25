using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format DTOs for the Maxio Advanced Billing REST API.
// Only the fields this integration consumes/produces are mapped.

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProductDto Product { get; set; } = new();
}

public sealed class MaxioProductDto
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
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerDto Customer { get; set; } = new();
}

public sealed class MaxioCustomerDto
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

public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

public sealed class MaxioCustomerAttributes
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

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionDto Subscription { get; set; } = new();
}

public sealed class MaxioSubscriptionDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductDto? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerDto? Customer { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscriptionAttributes
{
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    // "invoice" (remittance) billing: Maxio does not create a subscription with a
    // positive balance due at signup unless a payment method is on file. Invoice
    // collection enrolls the shopper without card capture; Maxio bills by invoice.
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

public sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
