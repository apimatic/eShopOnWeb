using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A user's enrollment (subscription) as recorded in the billing system of record.
/// </summary>
public sealed record SubscriptionSummary
{
    /// <summary>The subscription ID in the billing system of record.</summary>
    public int SubscriptionId { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>
    /// Raw billing-system state, e.g. active, trialing, past_due, canceled.
    /// See Maxio "Subscription States" for the full list and semantics.
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Recurring price the subscription renews at, in integer cents.</summary>
    public long PriceCents { get; init; }

    /// <summary>When the current period ends / the next regular charge is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
