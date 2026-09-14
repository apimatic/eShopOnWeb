using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>The specification's <c>Subscription-Response</c> schema.</summary>
public class SubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// The specification's <c>Subscription</c> schema, limited to the fields this integration consumes.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>One of the values of the specification's <c>Subscription-State</c> enumeration.</summary>
    public string? State { get; set; }

    public long BalanceInCents { get; set; }

    public long TotalRevenueInCents { get; set; }

    /// <summary>The recurring price of the subscribed product, in cents.</summary>
    public long ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When capture of the next payment will be attempted.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? DelayedCancelAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The reference assigned by the calling application.</summary>
    public string? Reference { get; set; }

    public string? Currency { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }

    public long? ProductPricePointId { get; set; }
}

/// <summary>The specification's <c>Create-Subscription-Request</c> schema.</summary>
public class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// The specification's <c>Create-Subscription</c> schema, limited to the fields this integration
/// sends. The product is identified by handle and the customer by its Maxio id; both are resolved
/// before the subscription is created.
/// </summary>
public class CreateSubscription
{
    public string? ProductHandle { get; set; }

    public long? CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    /// <summary>
    /// One of the specification's <c>Collection-Method</c> values. Signing up without capturing a
    /// payment method means the subscription has to be invoiced rather than charged.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The reference assigned by the calling application, used to recognise replays.</summary>
    public string? Reference { get; set; }
}
