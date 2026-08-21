using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

    public class ResendIdempotencyByKeySpecification : Specification<ResendIdempotencyRecord>, ISingleResultSpecification<ResendIdempotencyRecord>
{
    public ResendIdempotencyByKeySpecification(string idempotencyKey)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey);
    }
}
