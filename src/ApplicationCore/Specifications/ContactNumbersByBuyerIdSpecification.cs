using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerIdSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerIdSpecification(string buyerId, bool includeRemoved = false)
    {
        Query.Where(c => c.BuyerId == buyerId);

        if (!includeRemoved)
        {
            Query.Where(c => c.RemovedAt == null);
        }

        Query.OrderByDescending(c => c.CreatedAt);
    }
}
