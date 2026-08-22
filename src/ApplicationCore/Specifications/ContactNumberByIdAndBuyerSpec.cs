using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdAndBuyerSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int contactNumberId, string buyerId)
    {
        Query.Where(n => n.Id == contactNumberId && n.BuyerId == buyerId);
    }
}
