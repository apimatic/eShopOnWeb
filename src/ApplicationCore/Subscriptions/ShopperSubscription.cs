using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a shopper, as reflected by the billing system of record (Maxio).
/// </summary>
public class ShopperSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>The subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The stable handle of the plan (Maxio product) the shopper is subscribed to.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>The plan price captured on the subscription, in integer cents.</summary>
    public int PriceInCents { get; init; }

    /// <summary>How payment is collected, e.g. <c>remittance</c> or <c>automatic</c>.</summary>
    public string PaymentCollectionMethod { get; init; } = string.Empty;

    /// <summary>When the current billing period ends.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription will next be assessed/billed (the next billing date).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string FormattedPrice => Price.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
