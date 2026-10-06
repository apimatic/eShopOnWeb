using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A subscription a user purchased through Maxio Advanced Billing.
/// Mirrors the billing system of record; state is refreshed from Maxio on read.
/// </summary>
public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioSubscriptionId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public string ProductHandle { get; private set; }
    public string ProductName { get; private set; }
    public long ProductPriceInCents { get; private set; }
    public string Currency { get; private set; }
    public string State { get; private set; }
    public DateTime? NextBillingDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public UserSubscription(string userId, int maxioSubscriptionId, int maxioCustomerId,
        string productHandle, string productName, long productPriceInCents,
        string currency, string state, DateTime? nextBillingDateUtc)
    {
        UserId = userId;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        ProductHandle = productHandle;
        ProductName = productName;
        ProductPriceInCents = productPriceInCents;
        Currency = currency;
        State = state;
        NextBillingDateUtc = nextBillingDateUtc;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateFromBillingSystem(string state, long productPriceInCents,
        string currency, DateTime? nextBillingDateUtc)
    {
        State = state;
        ProductPriceInCents = productPriceInCents;
        Currency = currency;
        NextBillingDateUtc = nextBillingDateUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}