using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndIdSpecification : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(n => n.BuyerId == buyerId && n.Id == contactNumberId);
    }
}
