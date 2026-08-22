using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendByKeySpec : Specification<NotificationResendRecord>
{
    public NotificationResendByKeySpec(int notificationId, string idempotencyKey)
    {
        Query.Where(r => r.NotificationId == notificationId && r.IdempotencyKey == idempotencyKey);
    }
}
