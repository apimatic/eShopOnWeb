using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndPhoneSpecification : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpecification(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.PhoneNumber == canonicalPhoneNumber);
    }
}
