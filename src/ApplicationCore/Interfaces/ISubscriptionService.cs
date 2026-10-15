using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionPlansResult> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    long? TrialPriceInCents,
    long? InitialChargeInCents);

/// <summary>IsTruncated is true when the plan walk stopped at its page cap or deadline.</summary>
public record SubscriptionPlansResult(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionInfo(
    int? SubscriptionId,
    string PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate,
    bool DetailsAvailable);

public enum SubscribeOutcome
{
    Created,
    /// <summary>The user already had this subscription (idempotent repeat).</summary>
    AlreadySubscribed,
    /// <summary>Maxio's answer is unknown; a later call or GET /api/my-subscriptions settles it.</summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, SubscriptionInfo? Subscription);

public enum SubscriptionFailure
{
    UserNotFound,
    InvalidPlan,
    InProgress,
    Rejected,
    Unavailable
}

public class SubscriptionException : Exception
{
    public SubscriptionFailure Failure { get; }

    public SubscriptionException(SubscriptionFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }
}
