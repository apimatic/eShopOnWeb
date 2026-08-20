using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndPhoneSpec : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpec(string buyerId, string e164PhoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == e164PhoneNumber);
    }
}
