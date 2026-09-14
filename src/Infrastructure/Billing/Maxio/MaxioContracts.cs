using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

// Wire contracts for the Maxio Advanced Billing REST API. Property names map to the API's
// snake_case payloads through JsonNamingPolicy.SnakeCaseLower (see MaxioJson).
// Only the fields this integration reads or writes are modelled; unknown fields are ignored.

internal sealed record MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

internal sealed record MaxioProduct
{
    public long Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool RequireCreditCard { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public int? TrialPriceInCents { get; init; }
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public MaxioProductFamily? ProductFamily { get; init; }
}

internal sealed record MaxioProductFamily
{
    public long Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
}

internal sealed record MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; init; } = new();
}

internal sealed record MaxioCustomer
{
    public long Id { get; init; }
    public string? Reference { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

internal sealed record MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; init; } = new();
}

internal sealed record MaxioCreateCustomer
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

internal sealed record MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

internal sealed record MaxioSubscription
{
    public long Id { get; init; }
    public string? State { get; init; }
    public string? Reference { get; init; }
    public string? Currency { get; init; }
    public int ProductPriceInCents { get; init; }
    public string? PaymentCollectionMethod { get; init; }

    // The API spells the period start "started" and the period end "ends".
    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public MaxioProduct? Product { get; init; }
    public MaxioCustomer? Customer { get; init; }
}

internal sealed record MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; init; } = new();
}

internal sealed record MaxioCreateSubscription
{
    public string ProductHandle { get; init; } = string.Empty;
    public string CustomerReference { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; init; }
}

internal sealed record MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite Site { get; init; } = new();
}

internal sealed record MaxioSite
{
    public long Id { get; init; }
    public string? Name { get; init; }
    public string? Subdomain { get; init; }
    public string? Currency { get; init; }
    public bool Test { get; init; }
}

/// <summary>Error payload shape used by most Advanced Billing endpoints.</summary>
internal sealed record MaxioErrorListResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; init; }
}

/// <summary>Error payload shape used by the customer endpoints, where errors are keyed by field.</summary>
internal sealed record MaxioErrorMapResponse
{
    [JsonPropertyName("errors")]
    public Dictionary<string, List<string>>? Errors { get; init; }
}
