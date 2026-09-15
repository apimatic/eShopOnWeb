using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class ResendIdempotencyByKeySpecification : Specification<ResendIdempotencyRecord>
{
    public ResendIdempotencyByKeySpecification(string key)
    {
        Query.Where(r => r.Key == key);
    }
}
