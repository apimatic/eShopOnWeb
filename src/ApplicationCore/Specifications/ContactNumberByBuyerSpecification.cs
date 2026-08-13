using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single contact number, scoped to its owner so one shopper can never act on another's.</summary>
public class ContactNumberByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
