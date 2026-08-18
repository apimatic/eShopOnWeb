using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notification a resend request already produced under a given idempotency key, if any.
/// Used to make re-send idempotent: the same key must never send a second message.
/// </summary>
public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
