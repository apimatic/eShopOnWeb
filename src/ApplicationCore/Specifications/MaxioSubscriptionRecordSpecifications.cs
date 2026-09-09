using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioSubscriptionRecordForUserSpecification : Specification<MaxioSubscriptionRecord>
{
    public MaxioSubscriptionRecordForUserSpecification(string userId)
    {
        Query.Where(record => record.UserId == userId);
    }
}

public class MaxioSubscriptionRecordForUserAndPlanSpecification : Specification<MaxioSubscriptionRecord>
{
    public MaxioSubscriptionRecordForUserAndPlanSpecification(string userId, string productHandle)
    {
        Query.Where(record => record.UserId == userId && record.ProductHandle == productHandle);
    }
}
