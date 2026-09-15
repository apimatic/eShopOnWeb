using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId, bool newestFirst = false)
    {
        Query.Where(c => c.BuyerId == buyerId);

        if (newestFirst)
        {
            Query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id);
        }
        else
        {
            Query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);
        }
    }
}
