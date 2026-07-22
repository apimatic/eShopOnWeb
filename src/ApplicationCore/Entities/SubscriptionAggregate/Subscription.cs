using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb user's enrollment in a recurring plan: the link between the signed-in identity
/// and the billing provider's customer + subscription records. The provider stays the system of
/// record, so this aggregate is projected from it rather than persisted (see plan section 8).
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    private Subscription()
    {
        // required for materialization
        BuyerId = string.Empty;
    }

    public Subscription(string buyerId, int billingCustomerId, int billingSubscriptionId)
    {
        BuyerId = buyerId ?? throw new ArgumentNullException(nameof(buyerId));
        BillingCustomerId = billingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        Id = billingSubscriptionId;
    }

    /// <summary>The eShopOnWeb user reference - the signed-in user's email / username.</summary>
    public string BuyerId { get; private set; }

    public int BillingCustomerId { get; private set; }

    public int BillingSubscriptionId { get; private set; }

    public string PlanHandle { get; private set; } = string.Empty;

    public string PlanName { get; private set; } = string.Empty;

    /// <summary>The recurring plan price in the site currency (e.g. 299.00), not in minor units.</summary>
    public decimal PlanPrice { get; private set; }

    /// <summary>The provider's lifecycle state, e.g. active, on_hold, canceled.</summary>
    public string State { get; private set; } = string.Empty;

    public DateTimeOffset? NextBillingDate { get; private set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

    public bool CancelAtEndOfPeriod { get; private set; }

    /// <summary>Projects the provider's view of a subscription onto the eShopOnWeb user who owns it.</summary>
    public static Subscription FromBilling(string buyerId, BillingSubscription billing)
    {
        ArgumentNullException.ThrowIfNull(billing);

        return new Subscription(buyerId, billing.CustomerId, billing.Id)
        {
            PlanHandle = billing.ProductHandle ?? string.Empty,
            PlanName = billing.ProductName ?? string.Empty,
            PlanPrice = billing.ProductPrice,
            State = billing.State,
            NextBillingDate = billing.NextBillingAt,
            CurrentPeriodEndsAt = billing.CurrentPeriodEndsAt,
            CancelAtEndOfPeriod = billing.CancelAtEndOfPeriod
        };
    }
}
