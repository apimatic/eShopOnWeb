using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerIdSpec : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerIdSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.CreatedAt);
    }
}
