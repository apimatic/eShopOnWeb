using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;

/// <summary>
/// Local mapping from an eShopOnWeb user (buyer) to their Maxio Advanced Billing customer.
/// The buyer identity is carried on Maxio as <c>customer.reference</c> (provider-enforced unique);
/// this row caches the resolved numeric customer id so we do not re-resolve it on every request.
/// </summary>
public class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public MaxioCustomerLink(string buyerId, int maxioCustomerId)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        MaxioCustomerId = Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    // Required by EF Core.
    private MaxioCustomerLink()
    {
        BuyerId = string.Empty;
    }
}
