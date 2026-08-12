using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All contact numbers registered by a given shopper.</summary>
public sealed class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(cn => cn.BuyerId == buyerId)
             .OrderBy(cn => cn.CreatedDate);
    }
}
