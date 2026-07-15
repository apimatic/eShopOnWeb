using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A read model of a customer's billing-provider subscription, addressed by the provider's own
/// subscription id. There is no local persistence for this aggregate (see plan §8, "stateless mapping") —
/// it is always (re)built from the billing provider's current state via <see cref="IBillingClient"/>.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(
        int providerSubscriptionId,
        string userId,
        int providerCustomerId,
        string productHandle,
        int productId,
        string state,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? currentPeriodEndsAt)
    {
        Guard.Against.NegativeOrZero(providerSubscriptionId, nameof(providerSubscriptionId));
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        Guard.Against.NullOrEmpty(state, nameof(state));

        Id = providerSubscriptionId;
        UserId = userId;
        ProviderCustomerId = providerCustomerId;
        ProductHandle = productHandle;
        ProductId = productId;
        State = state;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
    }

    /// <summary>The eShopOnWeb user this subscription belongs to (email/username — see plan §4.4).</summary>
    public string UserId { get; private set; }

    /// <summary>The billing provider's customer id backing this subscription.</summary>
    public int ProviderCustomerId { get; private set; }

    /// <summary>The stable handle of the product/plan currently in effect.</summary>
    public string ProductHandle { get; private set; }

    /// <summary>The provider's numeric id for the current product/plan.</summary>
    public int ProductId { get; private set; }

    /// <summary>The provider's current lifecycle state (e.g. "active", "paused", "canceled").</summary>
    public string State { get; private set; }

    /// <summary>True when a delayed ("end of period") cancellation is scheduled.</summary>
    public bool CancelAtEndOfPeriod { get; private set; }

    /// <summary>The end of the current billing period, when known.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }
}
