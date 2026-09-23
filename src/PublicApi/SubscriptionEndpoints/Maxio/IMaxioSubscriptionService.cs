using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Identity of the eShopOnWeb user a subscription operation acts for, resolved from the JWT.
/// <see cref="EShopUserId"/> is the stable idempotency/reconciliation key for the Maxio customer.
/// </summary>
public sealed record MaxioUserContext(string EShopUserId, string Email, string FirstName, string LastName);

/// <summary>Classification of a subscription's provider state into an actionable outcome.</summary>
public enum SubscriptionOutcome
{
    /// <summary>Live and billable now (Maxio <c>active</c> / <c>trialing</c>).</summary>
    Active,

    /// <summary>Accepted but not yet live (<c>pending</c>, <c>assessing</c>, <c>awaiting_signup</c>, or an unreadable state). Never treated as success.</summary>
    Provisioning,

    /// <summary>In a problem or end-of-life state that needs attention (e.g. <c>past_due</c>, <c>canceled</c>, <c>failed_to_create</c>).</summary>
    AttentionRequired
}

/// <summary>A subscribable plan (a Maxio product in the configured product family).</summary>
public sealed record MaxioPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>A read-model view of a Maxio subscription for API responses.</summary>
public sealed record MaxioSubscriptionView(
    long Id,
    string? Reference,
    string State,
    SubscriptionOutcome Outcome,
    string? ProductHandle,
    string? ProductName,
    long? PriceInCents,
    System.DateTimeOffset? CurrentPeriodEndsAt,
    System.DateTimeOffset? NextBillingAt);

/// <summary>Result of a subscribe attempt.</summary>
/// <param name="Subscription">The resulting (new or pre-existing) subscription.</param>
/// <param name="AlreadySubscribed">True when a live subscription to the plan already existed and no new one was created.</param>
public sealed record SubscribeResult(MaxioSubscriptionView Subscription, bool AlreadySubscribed);

/// <summary>
/// The subscription-billing capability, backed by Maxio Advanced Billing as the system of record.
/// All operations are idempotent: customers and subscriptions are keyed by a deterministic
/// reference derived from <see cref="MaxioUserContext.EShopUserId"/>.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribable plans (products in the configured product family).</summary>
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and subscribes them to the given plan.
    /// Idempotent: a double-submit returns the existing live subscription rather than creating a second.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(MaxioUserContext user, string planHandle, CancellationToken ct);

    /// <summary>Lists the user's subscriptions in Maxio. Returns empty if the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<MaxioSubscriptionView>> GetMySubscriptionsAsync(MaxioUserContext user, CancellationToken ct);
}
