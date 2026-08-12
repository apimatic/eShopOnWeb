using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's registered numbers, newest first.</summary>
public sealed class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
             .OrderByDescending(c => c.RegisteredAt);
    }
}

/// <summary>A specific number, scoped to its owner so no shopper can act on another's.</summary>
public sealed class ContactNumberByIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
