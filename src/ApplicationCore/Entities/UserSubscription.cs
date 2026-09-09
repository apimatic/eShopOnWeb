using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local record linking an eShopOnWeb user to a subscription created in Maxio
/// Advanced Billing. Maxio remains the billing system of record; this entity is
/// the persisted userId-to-subscription mapping.
/// </summary>
public class UserSubscription : IAggregateRoot
{
    public int Id { get; private set; }
    public string UserId { get; private set; }
    public string UserName { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public string ProductHandle { get; private set; }
    public string ProductName { get; private set; }
    public string Currency { get; private set; }
    public int PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTime? NextBillingDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public UserSubscription(string userId,
        string userName,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string productHandle,
        string productName,
        string currency,
        int priceInCents,
        string state,
        DateTime? nextBillingDateUtc)
    {
        UserId = userId;
        UserName = userName;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        Currency = currency;
        PriceInCents = priceInCents;
        State = state;
        NextBillingDateUtc = nextBillingDateUtc;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SyncFromBillingSystem(string state, int priceInCents, string currency, DateTime? nextBillingDateUtc)
    {
        State = state;
        PriceInCents = priceInCents;
        Currency = currency;
        NextBillingDateUtc = nextBillingDateUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
