using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb identity user to the Maxio Advanced Billing customer
/// created on their behalf. The Maxio customer is created with the eShop user
/// id as its <c>reference</c>, making the link recoverable even if this record
/// is lost.
/// </summary>
public class MaxioCustomerLink : BaseEntity, IAggregateRoot
{
    public MaxioCustomerLink(string userId, int maxioCustomerId)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
    }

    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
}
