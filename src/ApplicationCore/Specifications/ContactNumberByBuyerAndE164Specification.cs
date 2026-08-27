using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndE164Specification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndE164Specification(string buyerId, string e164Number)
    {
        Query.Where(c => c.BuyerId == buyerId && c.E164Number == e164Number);
    }
}
