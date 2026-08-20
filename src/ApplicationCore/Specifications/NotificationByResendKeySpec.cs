using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByResendKeySpec : Specification<OrderNotification>
{
    public NotificationByResendKeySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == originalNotificationId
                         && n.ResendIdempotencyKey == idempotencyKey);
    }
}
