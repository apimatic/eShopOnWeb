using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<MySubscriptions> GetMySubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}

/// <summary>The authenticated shopper. First/last name are optional; they are derived from the email when absent.</summary>
public record Subscriber(string UserName, string Email, string? FirstName = null, string? LastName = null);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,

    /// <summary>The shopper already holds a subscription to this plan; it is returned unchanged.</summary>
    AlreadySubscribed,

    /// <summary>The request reached the provider but its outcome is not yet confirmed; it settles on a later read.</summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, string PlanHandle, string SubscriptionReference, BillingSubscription? Subscription);

/// <summary>An enrollment whose provider outcome has not been confirmed yet.</summary>
public record PendingEnrollment(string PlanHandle, string SubscriptionReference, DateTimeOffset RequestedAtUtc);

public record MySubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, PendingEnrollment? Pending);
