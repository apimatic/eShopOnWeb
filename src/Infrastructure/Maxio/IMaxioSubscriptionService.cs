using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>A plan (Maxio product) offered for subscription.</summary>
public class SubscriptionPlan
{
    /// <summary>Maxio product id, resolved from the live catalog at read time.</summary>
    public int MaxioProductId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
}

/// <summary>A subscription as seen from the eShopOnWeb side.</summary>
public class SubscriptionSummary
{
    public int MaxioSubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    /// <summary>Next date the subscriber will be billed, or null when Maxio has none.</summary>
    public System.DateTimeOffset? NextBillingDate { get; init; }
    public string CustomerReference { get; init; } = string.Empty;
}

public class SubscribeResult
{
    public SubscriptionSummary Subscription { get; init; } = new();
    /// <summary>
    /// True when an active subscription for this user+plan already existed and was
    /// returned instead of creating a second one (double-click / retry safety).
    /// </summary>
    public bool AlreadySubscribed { get; init; }
    /// <summary>True when a new Maxio customer record was created for this user.</summary>
    public bool CreatedCustomer { get; init; }
}

public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribable plans (Maxio products) in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given user (idempotent, keyed by reference),
    /// then subscribes them to the plan with the given handle. When no handle is supplied
    /// the first plan in the configured family is used. Returns the resulting subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userReference, string email, string? planHandle = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's active-ish Maxio subscriptions (empty when the user has no Maxio customer yet).</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
