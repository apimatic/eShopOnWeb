using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionLinkByUserAndProductSpec : Specification<SubscriptionLink>, ISingleResultSpecification<SubscriptionLink>
{
    public SubscriptionLinkByUserAndProductSpec(string userId, string productHandle)
    {
        Query.Where(x => x.UserId == userId && x.ProductHandle == productHandle);
    }
}
