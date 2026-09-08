using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Models;

/// <summary>
/// A Maxio subscription as seen by the shopper. Confirms plan, price, state and the next
/// billing date back to the client.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id (system of record).</summary>
    public long Id { get; init; }

    /// <summary>Maxio subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription is billable/current (not canceled, expired, etc.).</summary>
    public bool IsActive { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>The recurring amount the subscription is currently billed, in integer cents.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code when Maxio reports one (e.g. USD).</summary>
    public string? Currency { get; init; }

    /// <summary>When the next recurring charge will occur (alias of current period end / next assessment).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Maxio collection method for this subscription (e.g. remittance).</summary>
    public string? PaymentCollectionMethod { get; init; }
}
