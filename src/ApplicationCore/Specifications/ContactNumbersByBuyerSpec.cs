using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpec : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id);
    }
}
