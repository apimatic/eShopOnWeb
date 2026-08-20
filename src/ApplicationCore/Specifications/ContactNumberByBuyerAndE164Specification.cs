using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndE164Specification : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndE164Specification(string buyerId, string e164Number)
    {
        Query.Where(n => n.BuyerId == buyerId && n.E164Number == e164Number);
    }
}
