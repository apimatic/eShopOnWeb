using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a Maxio (Advanced Billing) subscription for an eShopOnWeb user.
/// Maxio remains the billing system of record; this entity is the persisted
/// userId-to-subscription mapping used for idempotency and fast lookups.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public string MaxioCustomerReference { get; private set; }
    public long MaxioCustomerId { get; private set; }
    public string MaxioSubscriptionReference { get; private set; }
    public long MaxioSubscriptionId { get; private set; }
    public string State { get; private set; }
    public int PriceInCents { get; private set; }
    public string Currency { get; private set; }
    public int BillingInterval { get; private set; }
    public string BillingIntervalUnit { get; private set; }
    public DateTime? NextBillingDateUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }

    public Subscription(string userId, string planHandle, string maxioCustomerReference,
        long maxioCustomerId, string maxioSubscriptionReference, long maxioSubscriptionId,
        string planName, string state, int priceInCents, string currency,
        int billingInterval, string billingIntervalUnit, DateTime? nextBillingDateUtc)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.NullOrEmpty(maxioCustomerReference, nameof(maxioCustomerReference));
        Guard.Against.NullOrEmpty(maxioSubscriptionReference, nameof(maxioSubscriptionReference));

        UserId = userId;
        PlanHandle = planHandle;
        MaxioCustomerReference = maxioCustomerReference;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionReference = maxioSubscriptionReference;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanName = planName;
        State = state;
        PriceInCents = priceInCents;
        Currency = currency;
        BillingInterval = billingInterval;
        BillingIntervalUnit = billingIntervalUnit;
        NextBillingDateUtc = nextBillingDateUtc;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Refreshes the local snapshot from the current state reported by Maxio.
    /// </summary>
    public void SyncFromMaxio(string planName, string state, int priceInCents, string currency,
        int billingInterval, string billingIntervalUnit, DateTime? nextBillingDateUtc,
        long maxioCustomerId, long maxioSubscriptionId)
    {
        PlanName = planName;
        State = state;
        PriceInCents = priceInCents;
        Currency = currency;
        BillingInterval = billingInterval;
        BillingIntervalUnit = billingIntervalUnit;
        NextBillingDateUtc = nextBillingDateUtc;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedUtc = DateTime.UtcNow;
    }
}