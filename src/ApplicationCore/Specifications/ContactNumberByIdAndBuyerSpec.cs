using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdAndBuyerSpec : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int contactNumberId, string buyerId)
    {
        Query.Where(n => n.Id == contactNumberId && n.BuyerId == buyerId);
    }
}
