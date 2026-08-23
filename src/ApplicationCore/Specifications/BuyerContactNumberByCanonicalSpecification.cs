using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerContactNumberByCanonicalSpecification : Specification<BuyerContactNumber>, ISingleResultSpecification
{
    public BuyerContactNumberByCanonicalSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.CanonicalNumber == canonicalNumber);
    }
}
