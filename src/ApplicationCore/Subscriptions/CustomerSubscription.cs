using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to an eShopOnWeb user, as reflected by the billing provider.
/// </summary>
public sealed record CustomerSubscription
{
    /// <summary>Provider subscription id.</summary>
    public int? Id { get; init; }

    /// <summary>The app-supplied reference stored on the subscription.</summary>
    public string? Reference { get; init; }

    /// <summary>Handle of the subscribed plan (product).</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Name of the subscribed plan.</summary>
    public string? PlanName { get; init; }

    /// <summary>Subscription state wire value (e.g. <c>active</c>, <c>trialing</c>, <c>awaiting_signup</c>).</summary>
    public string? State { get; init; }

    /// <summary>Recurring price of the subscribed product in integer cents.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>
    /// The next scheduled billing date — the end of the current period, when the next regularly
    /// scheduled charge occurs.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
