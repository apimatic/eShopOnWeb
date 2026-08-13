using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number, but only if it belongs to the given shopper. Scoping ownership into
/// the query means one shopper can never load another's number by id.
/// </summary>
public class ContactNumberByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}
