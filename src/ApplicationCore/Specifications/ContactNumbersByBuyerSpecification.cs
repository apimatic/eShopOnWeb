using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}
