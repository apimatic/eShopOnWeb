using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class LatestContactNumberForBuyerSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public LatestContactNumberForBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.Id);
    }
}
