using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
