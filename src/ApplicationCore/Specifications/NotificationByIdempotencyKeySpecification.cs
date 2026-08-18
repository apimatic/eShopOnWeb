using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds a message previously produced by a resend under a given idempotency key, so a repeat of the
/// same request returns that message instead of sending a second one.
/// </summary>
public class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
