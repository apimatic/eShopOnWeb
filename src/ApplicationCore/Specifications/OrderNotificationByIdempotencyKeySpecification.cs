using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The notification, if any, that a given resend idempotency key already produced.</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
