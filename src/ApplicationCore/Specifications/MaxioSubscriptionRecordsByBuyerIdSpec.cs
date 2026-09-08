using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioSubscriptionRecordsByBuyerIdSpec : Specification<MaxioSubscriptionRecord>
{
    public MaxioSubscriptionRecordsByBuyerIdSpec(string buyerId)
    {
        Query.Where(record => record.BuyerId == buyerId);
    }
}
