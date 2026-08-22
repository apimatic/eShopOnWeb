using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdAndBuyerSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(n => n.Id == id && n.BuyerId == buyerId);
    }
}
