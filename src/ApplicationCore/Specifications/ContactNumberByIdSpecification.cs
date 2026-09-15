using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number, scoped to its owner — the buyer filter is part of the query so a
/// caller can never load, use or delete another shopper's number.
/// </summary>
public class ContactNumberByIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
