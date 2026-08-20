using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndE164Spec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndE164Spec(string buyerId, string e164Number)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Number == e164Number);
    }
}
