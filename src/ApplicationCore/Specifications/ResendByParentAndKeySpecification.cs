using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByParentAndKeySpecification : Specification<OrderNotification>
{
    public ResendByParentAndKeySpecification(int parentNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == parentNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
