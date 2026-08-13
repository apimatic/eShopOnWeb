using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The resend record already produced under a given idempotency key, if any — so repeating a
/// resend request under the same key returns the existing message instead of sending a second one.
/// </summary>
public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
