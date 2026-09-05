using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlan(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);

public sealed record SubscriptionSummary(
    long Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record MaxioShopper(string UserId, string Email, string FirstName, string LastName)
{
    public string CustomerReference => $"eshoponweb-user:{UserId}";
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }

    public SubscriptionSummary ToSummary() => new(
        Id,
        Product?.Handle ?? string.Empty,
        Product?.Name ?? string.Empty,
        PriceInCents,
        State,
        NextAssessmentAt ?? CurrentPeriodEndsAt);
}

public sealed class CreateMaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateMaxioCustomer Customer { get; init; } = new();
}

public sealed class CreateMaxioCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}

public sealed class CreateMaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateMaxioSubscription Subscription { get; init; } = new();
}

public sealed class CreateMaxioSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; init; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; init; }

    // Remittance produces an invoice instead of attempting to charge a card.
    // This is required for the seeded cardless sandbox plans.
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; init; } = "remittance";

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(System.Net.HttpStatusCode statusCode)
        : base("Maxio Advanced Billing rejected the request.")
    {
        StatusCode = statusCode;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
}
