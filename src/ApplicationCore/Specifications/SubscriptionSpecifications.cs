using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionsByBuyerSpecification : Specification<SubscriptionRecord>
{
    public SubscriptionsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(s => s.BuyerId == buyerId)
            .OrderBy(s => s.Id);
    }
}

public sealed class SubscriptionByBuyerAndProductSpecification : Specification<SubscriptionRecord>
{
    public SubscriptionByBuyerAndProductSpecification(string buyerId, string productHandle)
    {
        Query.Where(s => s.BuyerId == buyerId && s.ProductHandle == productHandle);
    }
}
