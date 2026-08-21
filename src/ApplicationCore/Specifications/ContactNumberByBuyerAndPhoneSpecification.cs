using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndPhoneSpecification : Specification<BuyerContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.PhoneNumber == phoneNumber);
    }
}
