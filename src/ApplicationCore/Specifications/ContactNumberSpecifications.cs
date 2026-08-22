using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpec : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class ContactNumberByIdAndBuyerSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}
