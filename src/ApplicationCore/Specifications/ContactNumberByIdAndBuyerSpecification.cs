using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single contact number, scoped to its owner so one shopper cannot touch another's.</summary>
public class ContactNumberByIdAndBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
