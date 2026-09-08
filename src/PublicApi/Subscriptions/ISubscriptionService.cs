using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Orchestrates the shopper-facing subscription capability on top of the Maxio billing API.
/// It owns the idempotency guarantees: one Maxio customer per storefront user and one
/// subscription per (user, plan) pair.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans offered by the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper (idempotent) and subscribes them to the
    /// requested plan. Idempotent: repeated calls for the same shopper + plan return the existing
    /// subscription instead of creating duplicates.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(SubscriptionEnrollmentInput input, CancellationToken ct);

    /// <summary>Lists the shopper's Maxio subscriptions. Returns an empty list when the shopper has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string customerReference, CancellationToken ct);
}

/// <summary>Input for <see cref="ISubscriptionService.SubscribeAsync"/>.</summary>
public class SubscriptionEnrollmentInput
{
    /// <summary>The storefront user id; becomes the Maxio customer reference (unique in Maxio).</summary>
    public required string CustomerReference { get; init; }

    /// <summary>The shopper's email address (kept in sync on the Maxio customer).</summary>
    public required string Email { get; init; }

    /// <summary>Preferred billing first name; when omitted one is derived from the email.</summary>
    public string? FirstName { get; init; }

    /// <summary>Preferred billing last name; when omitted one is derived from the email.</summary>
    public string? LastName { get; init; }

    /// <summary>Handle of the plan to subscribe to (from the catalog / GET /api/subscription-plans).</summary>
    public required string PlanHandle { get; init; }
}

/// <summary>Result of <see cref="ISubscriptionService.SubscribeAsync"/>.</summary>
public class SubscriptionEnrollmentResult
{
    /// <summary>True when a brand new Maxio subscription was created; false when an existing one was returned (idempotent repeat).</summary>
    public required bool CreatedNow { get; init; }

    public required SubscriptionDto Subscription { get; init; }
}
