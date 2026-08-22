using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperContactNumberByIdSpecification : Specification<ShopperContactNumber>
{
    public ShopperContactNumberByIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
