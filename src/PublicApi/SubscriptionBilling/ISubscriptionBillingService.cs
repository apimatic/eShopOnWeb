using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Recurring-subscription billing, with Maxio Advanced Billing as the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default);
}

/// <summary>The eShopOnWeb user, as identified by the caller's token.</summary>
public record BillingUser(string UserName, string Email);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequireCreditCard);

/// <summary>
/// The plans on offer. <see cref="Truncated"/> is true when the provider had more products than the
/// page cap allowed us to read, so the list may be incomplete.
/// </summary>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

public record SubscriptionDetails(
    int SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,

    /// <summary>The user already had this subscription (double submit / repeat request); nothing new was created.</summary>
    AlreadySubscribed,

    /// <summary>
    /// The provider may have accepted the request but we could not confirm it (connection lost).
    /// The next request for the same plan settles it; it is never reported as a failure.
    /// </summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, string Reference, SubscriptionDetails? Subscription);
