using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local mirror of a Maxio Advanced Billing subscription, keyed by the eShopOnWeb user.
/// Maxio remains the billing system of record; this row exists to persist the
/// userId &lt;-&gt; Maxio customer/subscription mapping and to serve "my subscriptions" quickly.
/// </summary>
public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string CustomerReference { get; private set; }
    public long MaxioCustomerId { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string SubscriptionReference { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public int PriceInCents { get; private set; }
    public string Currency { get; private set; }
    public string State { get; private set; }
    public DateTime? NextBillingAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSyncedAtUtc { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private UserSubscription() { }
#pragma warning restore CS8618

    public UserSubscription(
        string userId,
        MaxioCustomer customer,
        MaxioSubscription subscription)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Null(customer, nameof(customer));
        Guard.Against.Null(subscription, nameof(subscription));

#pragma warning disable CS8618 // Remaining state is initialized by UpdateFrom below.
        UserId = userId;
        CustomerReference = customer.Reference;
        MaxioCustomerId = customer.CustomerId;
        CreatedAtUtc = DateTime.UtcNow;
#pragma warning restore CS8618
        UpdateFrom(subscription);
    }

    /// <summary>
    /// Refreshes the mirrored state from the billing system of record.
    /// </summary>
    public void UpdateFrom(MaxioSubscription subscription)
    {
        Guard.Against.Null(subscription, nameof(subscription));

        if (subscription.SubscriptionId != MaxioSubscriptionId && MaxioSubscriptionId != 0)
        {
            throw new ArgumentException($"Subscription id mismatch: mirror holds {MaxioSubscriptionId}, Maxio returned {subscription.SubscriptionId}.");
        }

        MaxioSubscriptionId = subscription.SubscriptionId;
        SubscriptionReference = subscription.Reference;
        PlanHandle = subscription.PlanHandle;
        PlanName = subscription.PlanName;
        PriceInCents = subscription.PriceInCents;
        Currency = subscription.Currency;
        State = subscription.State;
        NextBillingAtUtc = (subscription.NextBillingAt ?? subscription.CurrentPeriodEndsAt)?.UtcDateTime;
        ActivatedAtUtc = subscription.ActivatedAt?.UtcDateTime;
        CanceledAtUtc = subscription.CanceledAt?.UtcDateTime;
        LastSyncedAtUtc = DateTime.UtcNow;
    }
}
