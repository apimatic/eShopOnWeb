using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A shopper's registration of a specific canonical number, used to avoid registering the same
/// number twice for the same shopper.
/// </summary>
public class ContactNumberByBuyerAndValueSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndValueSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}
