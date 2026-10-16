using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShop buyer to their customer record in the billing system.
/// The row doubles as the claim that stops two concurrent requests from both creating a billing customer:
/// its key is the buyer id, so the store refuses a second insert for the same buyer.
/// </summary>
public class BillingCustomer : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string buyerId, string customerReference, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        BuyerId = buyerId;
        CustomerReference = customerReference;
        ClaimedAt = claimedAt;
        UpdatedAt = claimedAt;
    }

    public string BuyerId { get; private set; }

    /// <summary>The reference this app sends to the billing system; deterministic per buyer.</summary>
    public string CustomerReference { get; private set; }

    /// <summary>The billing system's id for the customer; null while the claim is still being fulfilled.</summary>
    public int? BillingCustomerId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void LinkBillingCustomer(int billingCustomerId, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(billingCustomerId, nameof(billingCustomerId));
        BillingCustomerId = billingCustomerId;
        UpdatedAt = now;
    }

    public void Renew(DateTimeOffset now)
    {
        ClaimedAt = now;
        UpdatedAt = now;
    }
}
