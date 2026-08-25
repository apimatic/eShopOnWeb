using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire DTOs for the Maxio Advanced Billing JSON API. Field names verified against the
// official Maxio API documentation (developers.maxio.com / maxio-com SDK docs).

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

internal sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductResponse
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; set; }
    [JsonPropertyName("activated_at")] public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_reference")] public string CustomerReference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = string.Empty;
}

internal sealed class MaxioErrorListResponse
{
    [JsonPropertyName("errors")] public List<string>? Errors { get; set; }
}
