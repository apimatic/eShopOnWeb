using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId, bool activeOnly = true)
    {
        Query.Where(c => c.BuyerId == buyerId);

        if (activeOnly)
        {
            Query.Where(c => c.IsActive);
        }

        Query.OrderByDescending(c => c.Id);
    }
}
