using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription, projected from a Maxio subscription. Used both to confirm a
/// freshly created subscription and to list a customer's existing subscriptions.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Billing-system subscription id.</summary>
    public int Id { get; init; }

    /// <summary>Lifecycle state as reported by the billing system (e.g. <c>active</c>, <c>trialing</c>).</summary>
    public string? State { get; init; }

    /// <summary>Handle of the plan the subscription is on.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Human-readable plan/product name.</summary>
    public string? PlanName { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Convenience decimal rendering of <see cref="PriceInCents"/> (major units).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code (available on subscriptions, unlike plans).</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// End of the current billing period. Maxio does not return a dedicated "next billing"
    /// field on the subscription; the current period end is the next billing/assessment date.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>Stable customer reference (the eShop username/email) this subscription belongs to.</summary>
    public string? CustomerReference { get; init; }
}
