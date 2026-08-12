using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A notification previously produced under a given resend idempotency key. Lets a repeated resend
/// under the same key return the earlier result instead of sending a second message.
/// </summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
