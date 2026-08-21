using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local projection of a Maxio subscription. Maxio remains the system of record.
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    private SubscriptionRecord() { }

    public SubscriptionRecord(
        string userId,
        string planHandle,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string customerReference,
        string subscriptionReference)
    {
        UserId = userId;
        PlanHandle = planHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
    }

    public string UserId { get; private set; } = string.Empty;
    public string PlanHandle { get; private set; } = string.Empty;
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = string.Empty;
    public long PriceInCents { get; private set; }
    public string State { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; private set; }
    public DateTimeOffset SyncedAt { get; private set; }

    public void Reconcile(
        int maxioCustomerId,
        int maxioSubscriptionId,
        string planName,
        long priceInCents,
        string state,
        string currency,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset syncedAt)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        Currency = currency;
        NextBillingAt = nextBillingAt;
        SyncedAt = syncedAt;
    }
}
