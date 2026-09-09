using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a recurring subscription created in the external billing system
/// (Maxio Advanced Billing) for an eShopOnWeb buyer.
/// The external billing system remains the system of record; this entity is a
/// projection used for fast lookups and reconciliation.
/// </summary>
public class SubscriptionRecord : IAggregateRoot
{
    public int Id { get; private set; }

    /// <summary>
    /// The eShopOnWeb buyer (ApplicationUser.Id).
    /// </summary>
    public string BuyerId { get; private set; } = string.Empty;

    /// <summary>
    /// Customer id in the external billing system.
    /// </summary>
    public int BillingCustomerId { get; private set; }

    /// <summary>
    /// Subscription id in the external billing system.
    /// </summary>
    public int BillingSubscriptionId { get; private set; }

    /// <summary>
    /// Handle of the subscribed plan (Maxio product handle).
    /// </summary>
    public string PlanHandle { get; private set; } = string.Empty;

    public string PlanName { get; private set; } = string.Empty;

    public int PriceInCents { get; private set; }

    /// <summary>
    /// Subscription state as last reported by the external billing system
    /// (e.g. active, trialing, canceled, past_due).
    /// </summary>
    public string State { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public SubscriptionRecord(string buyerId, int billingCustomerId, int billingSubscriptionId,
        string planHandle, string planName, int priceInCents, string state)
    {
        BuyerId = buyerId;
        BillingCustomerId = billingCustomerId;
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateState(string state, int priceInCents)
    {
        State = state;
        PriceInCents = priceInCents;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
