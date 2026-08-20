using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpec : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderByDescending(n => n.Id);
    }
}

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.CanonicalNumber == canonicalNumber);
    }
}

public class ContactNumberByIdAndBuyerSpec : Specification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(n => n.Id == id && n.BuyerId == buyerId);
    }
}
