using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system of record.
/// </summary>
/// <param name="Id">Billing system identifier of the subscription.</param>
/// <param name="Reference">The reference this application assigned to the subscription; the idempotency anchor.</param>
/// <param name="State">Lifecycle state reported by the billing system, e.g. <c>active</c> or <c>canceled</c>.</param>
/// <param name="PlanHandle">Handle of the plan the shopper is enrolled in.</param>
/// <param name="PlanName">Display name of the plan.</param>
/// <param name="Price">Recurring price per billing period, in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO 4217 currency code the subscription is billed in.</param>
/// <param name="CurrentPeriodStartedAt">Start of the billing period currently in progress.</param>
/// <param name="CurrentPeriodEndsAt">End of the billing period currently in progress.</param>
/// <param name="NextBillingAt">When the subscription will next be assessed. <c>null</c> once it no longer renews.</param>
/// <param name="CanceledAt">When the subscription was canceled, when applicable.</param>
/// <param name="CreatedAt">When the subscription was created in the billing system.</param>
/// <param name="PaymentCollectionMethod">How the billing system collects payment, e.g. <c>automatic</c> or <c>remittance</c>.</param>
public record CustomerSubscription(
    long Id,
    string? Reference,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string Currency,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CanceledAt,
    DateTimeOffset? CreatedAt,
    string? PaymentCollectionMethod)
{
    /// <summary>
    /// True while the subscription still represents a live commercial relationship, i.e. it has not
    /// reached a terminal state. Used to decide whether a repeated subscribe attempt is a duplicate.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
