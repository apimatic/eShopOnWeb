using Ardalis.GuardClauses;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
