using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndIdSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndIdSpec(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == contactNumberId);
    }
}
