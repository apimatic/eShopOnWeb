using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class MaxioSubscriptionRecordSpecification : Specification<MaxioSubscriptionRecord>,
    ISingleResultSpecification<MaxioSubscriptionRecord>
{
    public MaxioSubscriptionRecordSpecification(string userId, string productHandle)
    {
        Query.Where(record => record.UserId == userId && record.ProductHandle == productHandle);
    }
}
