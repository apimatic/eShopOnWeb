using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's registered numbers, newest first.</summary>
public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.RegisteredDate);
    }
}

/// <summary>A single number, but only if it belongs to the given shopper.</summary>
public class ContactNumberByIdForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
