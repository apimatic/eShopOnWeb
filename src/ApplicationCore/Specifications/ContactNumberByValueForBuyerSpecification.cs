using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's already-registered number with a given canonical value, used to avoid duplicates.</summary>
public class ContactNumberByValueForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByValueForBuyerSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}
