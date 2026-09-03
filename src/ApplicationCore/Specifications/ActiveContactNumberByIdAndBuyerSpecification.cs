using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveContactNumberByIdAndBuyerSpecification : Specification<ContactNumber>
{
    public ActiveContactNumberByIdAndBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId && c.DeletedAt == null);
    }
}
