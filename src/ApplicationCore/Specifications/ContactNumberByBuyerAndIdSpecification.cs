using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndIdSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(n => n.BuyerId == buyerId && n.Id == contactNumberId);
    }
}
