using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerIdSpec : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerIdSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}

public class ContactNumberByIdAndBuyerSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
