using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndIdSpec : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndIdSpec(string buyerId, int contactNumberId)
    {
        Query.Where(n => n.BuyerId == buyerId && n.Id == contactNumberId);
    }
}
