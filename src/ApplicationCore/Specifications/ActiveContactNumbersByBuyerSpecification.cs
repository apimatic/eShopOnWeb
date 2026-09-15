using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ActiveContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt);
    }
}
