using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number, scoped to its owner so one shopper can never read or delete another's.
/// </summary>
public class ContactNumberByBuyerAndIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(cn => cn.BuyerId == buyerId && cn.Id == contactNumberId);
    }
}
