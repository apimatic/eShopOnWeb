using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's registered contact numbers.</summary>
public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query
            .Where(c => c.BuyerId == buyerId)
            .OrderBy(c => c.RegisteredDate);
    }
}
