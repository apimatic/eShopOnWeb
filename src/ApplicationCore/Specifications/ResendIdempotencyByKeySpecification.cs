using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a prior resend recorded under a given idempotency key.</summary>
public class ResendIdempotencyByKeySpecification : Specification<ResendIdempotencyRecord>
{
    public ResendIdempotencyByKeySpecification(string idempotencyKey)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey);
    }
}
