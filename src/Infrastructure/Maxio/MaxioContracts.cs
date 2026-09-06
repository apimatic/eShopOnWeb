using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire contracts for the Maxio Advanced Billing REST API. Property names are mapped from
// snake_case by the serializer options in MaxioJson. Only the fields this integration relies
// on are modelled; unknown fields are ignored.

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

// --- request bodies ---

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

internal sealed class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Provider-side duplicate prevention: a repeat of the same token within 60 minutes is
    /// rejected with 409 instead of creating a second subscription.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
