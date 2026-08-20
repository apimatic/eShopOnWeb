using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdAndBuyerSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByIdAndBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(n => n.Id == contactNumberId && n.BuyerId == buyerId);
    }
}
