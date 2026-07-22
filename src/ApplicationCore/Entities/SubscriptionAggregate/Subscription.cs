using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb view of a recurring subscription held by the billing provider.
/// The provider remains the system of record; this aggregate links an eShopOnWeb user
/// to the provider-side customer and subscription identifiers.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(string userReference,
        int billingCustomerId,
        int billingSubscriptionId,
        string planHandle,
        string planName,
        decimal planPrice,
        string state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        bool cancelAtEndOfPeriod)
    {
        UserReference = Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        BillingCustomerId = billingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle ?? string.Empty;
        PlanName = planName ?? string.Empty;
        PlanPrice = planPrice;
        State = Guard.Against.NullOrEmpty(state, nameof(state));
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
    }

    /// <summary>The stable eShopOnWeb user reference (email / username) — see plan §4.4.</summary>
    public string UserReference { get; private set; }
    public int BillingCustomerId { get; private set; }
    public int BillingSubscriptionId { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }

    /// <summary>The recurring plan price expressed in major currency units (e.g. 299.00).</summary>
    public decimal PlanPrice { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }
    public DateTimeOffset? NextBillingAt { get; private set; }
    public bool CancelAtEndOfPeriod { get; private set; }
}
