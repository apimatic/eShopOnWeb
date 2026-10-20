using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents, int? Interval, string? IntervalUnit);

/// <param name="IsTruncated">True when a paging cap stopped the read before the provider ran out of plans.</param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionSummary(
    string Id,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this call.</summary>
    Created,
    /// <summary>The shopper was already subscribed (repeat/double submit); the existing subscription is returned.</summary>
    AlreadySubscribed,
    /// <summary>The provider call's outcome could not be confirmed yet; no subscription to show.</summary>
    PendingConfirmation
}

public record SubscribeResult(SubscribeOutcome Outcome, SubscriptionSummary? Subscription);

public enum BillingFailure
{
    InvalidPlan,
    NotFound,
    InProgress,
    /// <summary>The provider refused the request itself (a 4xx the caller can act on).</summary>
    Rejected,
    /// <summary>The provider is unreachable, misconfigured or failing: nothing the caller can fix.</summary>
    Unavailable,
    /// <summary>A write may have landed (transport failure, unreadable 2xx). Settled by the service, never surfaced.</summary>
    Indeterminate
}

public class BillingException : Exception
{
    public BillingFailure Failure { get; }
    public int? ProviderStatus { get; }

    public BillingException(BillingFailure failure, string message, int? providerStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        ProviderStatus = providerStatus;
    }
}

/// <summary>Recurring-subscription billing; the billing provider is the system of record.</summary>
public interface ISubscriptionBilling
{
    Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
