using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndPhoneSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}
