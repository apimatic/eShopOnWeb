using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models mapping 1:1 to the Maxio Billing API JSON payloads. These stay internal to the
// Infrastructure layer; callers work with the ApplicationCore domain models instead.

internal sealed record MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("reference")] public string? Reference { get; init; }
    [JsonPropertyName("first_name")] public string? FirstName { get; init; }
    [JsonPropertyName("last_name")] public string? LastName { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
}

internal sealed record MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; init; }
}

internal sealed record MaxioCreateCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; init; } = string.Empty;
}

internal sealed record MaxioCreateCustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCreateCustomer Customer { get; init; } = new();
}

internal sealed record MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("handle")] public string? Handle { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; init; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
}

internal sealed record MaxioProductEnvelope
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; init; }
}

internal sealed record MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_started_at")] public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; init; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; init; }
}

internal sealed record MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; init; }
}

internal sealed record MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; init; } = string.Empty;
    [JsonPropertyName("customer_id")] public int CustomerId { get; init; }
    [JsonPropertyName("uniqueness_token")] public string? UniquenessToken { get; init; }
}

internal sealed record MaxioCreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioCreateSubscription Subscription { get; init; } = new();
}
