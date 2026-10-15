using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb buyer to their customer record in the billing system.
/// The row doubles as the claim that serializes customer creation: it is inserted (keyed by
/// <see cref="BuyerId"/>) before the billing customer is created, so a concurrent second insert
/// is refused by the primary key.
/// </summary>
public class BillingCustomer : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string buyerId, string customerReference, DateTimeOffset claimedAt)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        CustomerReference = Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        ClaimedAt = claimedAt;
    }

    public string BuyerId { get; private set; }

    /// <summary>The reference this application assigns to the billing customer (unique per billing site).</summary>
    public string CustomerReference { get; private set; }

    /// <summary>The billing system's customer id; null while creation is in flight or unresolved.</summary>
    public int? BillingCustomerId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset? LinkedAt { get; private set; }

    public bool IsLinked => BillingCustomerId.HasValue;

    public void Link(int billingCustomerId, DateTimeOffset linkedAt)
    {
        BillingCustomerId = Guard.Against.NegativeOrZero(billingCustomerId, nameof(billingCustomerId));
        LinkedAt = linkedAt;
    }

    public bool IsClaimFresh(DateTimeOffset now, TimeSpan lease) => !IsLinked && ClaimedAt + lease > now;
}
