using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdAndBuyerSpec : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByIdAndBuyerSpec(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
