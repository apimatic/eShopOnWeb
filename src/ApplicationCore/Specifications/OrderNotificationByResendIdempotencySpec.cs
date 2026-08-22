using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByResendIdempotencySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByResendIdempotencySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
