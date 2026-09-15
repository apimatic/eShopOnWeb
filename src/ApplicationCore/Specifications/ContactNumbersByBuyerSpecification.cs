using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.RegisteredAt);
    }
}

public sealed class ContactNumberByIdForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForBuyerSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}
