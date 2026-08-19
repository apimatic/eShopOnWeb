using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notification, if any, that a re-send already produced under a given idempotency key.
/// Used to make re-sends idempotent: a repeat of the same key returns the existing message.
/// </summary>
public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
