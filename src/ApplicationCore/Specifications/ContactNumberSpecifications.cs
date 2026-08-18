using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All contact numbers registered by a given shopper.</summary>
public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
             .OrderBy(c => c.RegisteredAt);
    }
}

/// <summary>A single contact number, scoped to its owner so no shopper can act on another's.</summary>
public class ContactNumberByIdForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
