using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId, bool activeOnly = true)
    {
        Query.Where(n => n.BuyerId == buyerId);

        if (activeOnly)
        {
            Query.Where(n => !n.IsDeleted);
        }

        Query.OrderByDescending(n => n.CreatedAt);
    }
}
