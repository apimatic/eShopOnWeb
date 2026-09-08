using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Persists the mapping between an eShopOnWeb buyer (the identity from the JWT)
/// and the Maxio Advanced Billing customer created for that buyer.
/// </summary>
public class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
    public MaxioCustomerLink(string buyerId, long maxioCustomerId)
    {
        BuyerId = buyerId;
        MaxioCustomerId = maxioCustomerId;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

#pragma warning disable CS8618 // Required by EF
    private MaxioCustomerLink() { }
#pragma warning restore CS8618

    /// <summary>
    /// The eShopOnWeb username carried in the JWT (unique per buyer).
    /// </summary>
    public string BuyerId { get; private set; }

    /// <summary>
    /// The customer id assigned by Maxio Advanced Billing.
    /// </summary>
    public long MaxioCustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateCustomerId(long maxioCustomerId)
    {
        MaxioCustomerId = maxioCustomerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
