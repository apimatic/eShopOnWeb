using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId, bool includeDeleted = false)
    {
        Query.Where(c => c.BuyerId == buyerId);

        if (!includeDeleted)
        {
            Query.Where(c => !c.IsDeleted);
        }

        Query.OrderByDescending(c => c.CreatedAt);
    }
}
