using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// A customer's subscription as surfaced to the rest of eShopOnWeb by <see cref="IBillingClient"/>.
/// </summary>
public sealed record BillingSubscription
{
    public required int Id { get; init; }
    public required int BillingCustomerId { get; init; }
    public required BillingSubscriptionState State { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public required int PriceInCents { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>True when an end-of-period cancellation is scheduled (subscription is still active until then).</summary>
    public bool CancelAtPeriodEnd { get; init; }

    /// <summary>The plan a delayed ("at next renewal") plan change will switch to, if one is scheduled.</summary>
    public string? PendingPlanHandle { get; init; }
}
