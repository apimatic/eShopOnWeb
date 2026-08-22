using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.OriginalNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
