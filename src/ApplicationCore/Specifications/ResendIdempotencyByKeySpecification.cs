using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The record of a previous resend under a given idempotency key, if any.</summary>
public class ResendIdempotencyByKeySpecification : Specification<ResendIdempotencyRecord>
{
    public ResendIdempotencyByKeySpecification(string idempotencyKey)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey);
    }
}
