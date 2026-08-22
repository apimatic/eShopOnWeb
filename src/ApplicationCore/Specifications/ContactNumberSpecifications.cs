using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpec : Specification<ShopperContactNumber>
{
    public ContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderBy(c => c.Id);
    }
}

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}

public class ContactNumberByBuyerAndIdSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndIdSpec(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}
