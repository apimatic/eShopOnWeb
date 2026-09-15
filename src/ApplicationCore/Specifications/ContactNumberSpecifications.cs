using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId, bool activeOnly = true)
    {
        Query.Where(c => c.BuyerId == buyerId);

        if (activeOnly)
        {
            Query.Where(c => c.IsActive);
        }

        Query.OrderByDescending(c => c.CreatedAt);
    }
}

public class ContactNumberByBuyerAndCanonicalSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}

public class ContactNumberByIdForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForBuyerSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}
