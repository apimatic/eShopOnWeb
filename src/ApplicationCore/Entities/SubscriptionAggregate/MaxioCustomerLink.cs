using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local cache mapping an eShopOnWeb identity to the Maxio customer created for it.
/// Maxio remains the system of record; this row only avoids a reference lookup on every call
/// and gives GetMySubscriptionsAsync a starting point.
/// </summary>
public class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerLink() { }
#pragma warning restore CS8618

    public MaxioCustomerLink(string buyerId, int maxioCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        MaxioCustomerId = maxioCustomerId;
    }

    public string BuyerId { get; private set; }
    public int MaxioCustomerId { get; private set; }
}
