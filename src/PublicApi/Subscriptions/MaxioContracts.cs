using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioCustomerRequestEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerRequest Customer { get; init; } = new();
}

public sealed class MaxioCustomerRequest
{
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioCustomerResponseEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("reference")] public string? Reference { get; init; }
    [JsonPropertyName("first_name")] public string? FirstName { get; init; }
    [JsonPropertyName("last_name")] public string? LastName { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
}

public sealed class MaxioProductResponseEnvelope
{
    [JsonPropertyName("product")] public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("handle")] public string? Handle { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; init; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; init; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("handle")] public string? Handle { get; init; }
}

public sealed class MaxioSubscriptionRequestEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscriptionRequest Subscription { get; init; } = new();
}

public sealed class MaxioSubscriptionRequest
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; init; } = string.Empty;
    [JsonPropertyName("customer_id")] public int CustomerId { get; init; }
    [JsonPropertyName("reference")] public string Reference { get; init; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; init; } = "remittance";
}

public sealed class MaxioSubscriptionResponseEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("product_price_in_cents")] public long PriceInCents { get; init; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("reference")] public string? Reference { get; init; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; init; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; init; }
}
