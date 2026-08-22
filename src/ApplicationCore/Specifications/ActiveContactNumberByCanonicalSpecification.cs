using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveContactNumberByCanonicalSpecification : Specification<ShopperContactNumber>
{
    public ActiveContactNumberByCanonicalSpecification(string canonicalNumber)
    {
        Query.Where(c => c.CanonicalNumber == canonicalNumber && c.IsActive);
    }
}
