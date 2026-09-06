using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Subscription Response</c> envelope.</summary>
public class SubscriptionResponse
{
    public Subscription? Subscription { get; set; }
}

/// <summary>Maxio <c>Subscription</c> schema (subset consumed by this integration).</summary>
public class Subscription
{
    public long Id { get; set; }

    /// <summary>One of the values of the specification's <c>Subscription State</c> enum.</summary>
    public string? State { get; set; }

    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }

    /// <summary>The recurring amount of the product version currently subscribed, in integer cents.</summary>
    public long ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When capture of payment will be tried or retried — i.e. the next billing date.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }
    public string? CancellationMessage { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Currency { get; set; }
    public string? Reference { get; set; }
    public long? CurrentBillingAmountInCents { get; set; }

    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
}

/// <summary>Maxio <c>Create Subscription Request</c> body.</summary>
public class CreateSubscriptionRequest
{
    public CreateSubscriptionRequest(CreateSubscription subscription) => Subscription = subscription;

    public CreateSubscription Subscription { get; set; }
}

/// <summary>
/// Maxio <c>Create Subscription</c> schema. Only the members this integration sets are modelled;
/// <c>null</c> members are omitted from the payload.
/// </summary>
public class CreateSubscription
{
    /// <summary>The API handle of the product being subscribed to.</summary>
    public string? ProductHandle { get; set; }

    /// <summary>The reference value of an existing customer within Maxio.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>The id of an existing customer within Maxio.</summary>
    public int? CustomerId { get; set; }

    /// <summary>The reference value, provided by the calling application, for the subscription itself.</summary>
    public string? Reference { get; set; }

    /// <summary>A value of the specification's <c>Collection Method</c> enum.</summary>
    public string? PaymentCollectionMethod { get; set; }
}
