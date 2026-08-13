using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds a notification previously produced under a given resend idempotency key, so that a
/// repeated resend request under the same key returns the existing message instead of sending anew.
/// </summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
