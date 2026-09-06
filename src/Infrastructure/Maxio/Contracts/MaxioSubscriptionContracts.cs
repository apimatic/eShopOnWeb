using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio <c>Subscription</c> (<c>components/schemas/Subscription.yaml</c>). Only the fields this
/// integration reads are modelled.
/// </summary>
public record MaxioSubscription
{
    public int Id { get; init; }
    public string? State { get; init; }
    public string? Reference { get; init; }
    public long BalanceInCents { get; init; }
    public long TotalRevenueInCents { get; init; }
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next attempt to bill. This is the "next billing date".</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? TrialStartedAt { get; init; }
    public DateTimeOffset? TrialEndedAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? CancellationMessage { get; init; }
    public bool? CancelAtEndOfPeriod { get; init; }
    public string? PaymentCollectionMethod { get; init; }
    public string? Currency { get; init; }
    public int? ProductPricePointId { get; init; }
    public MaxioCustomer? Customer { get; init; }
    public MaxioProduct? Product { get; init; }
}

/// <summary>Maxio <c>Subscription Response</c> (<c>components/schemas/Subscription-Response.yaml</c>).</summary>
public record MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; init; }
}

/// <summary>
/// Maxio <c>Create Subscription</c> (<c>components/schemas/Create-Subscription.yaml</c>).
/// <para>
/// Only the members this integration sets are modelled. The plan is identified by
/// <see cref="ProductHandle"/> - the spec recommends the API handle over the unpublished numeric
/// product id - and the customer by <see cref="CustomerId"/>, which we always have because the
/// customer is ensured before signup.
/// </para>
/// </summary>
public record MaxioCreateSubscription
{
    public required string ProductHandle { get; init; }

    public int? CustomerId { get; init; }

    /// <summary>Reference value provided by this application for the subscription itself.</summary>
    public string? Reference { get; init; }

    /// <summary>
    /// How the subscription is collected - one of the values in
    /// <c>components/schemas/Collection-Method.yaml</c>. eShopOnWeb captures no card details, so
    /// signups are invoiced rather than charged.
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }
}

/// <summary>Maxio <c>Create Subscription Request</c> (<c>components/schemas/Create-Subscription-Request.yaml</c>).</summary>
public record MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required MaxioCreateSubscription Subscription { get; init; }
}
