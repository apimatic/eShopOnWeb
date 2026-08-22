using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdAndBuyerIdSpecification : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByIdAndBuyerIdSpecification(int contactNumberId, string buyerId, bool includeRemoved = false)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);

        if (!includeRemoved)
        {
            Query.Where(c => c.RemovedAt == null);
        }
    }
}
