using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == sourceNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}
