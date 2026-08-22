using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndPhoneSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndPhoneSpec(string buyerId, string phoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.PhoneNumber == phoneNumber);
    }
}
