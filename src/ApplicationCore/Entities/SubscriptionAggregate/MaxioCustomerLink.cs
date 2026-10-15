using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb user to the Maxio customer created for them. The row is inserted (claimed)
/// before the Maxio customer is created; the primary key refuses a second claim for the same user.
/// </summary>
public class MaxioCustomerLink
{
    public string BuyerId { get; private set; }
    public string Reference { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public MaxioCustomerLink(string buyerId, string reference, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        Reference = reference;
        CreatedAt = createdAt;
    }

    public void Resolve(int maxioCustomerId) => MaxioCustomerId = maxioCustomerId;
}
