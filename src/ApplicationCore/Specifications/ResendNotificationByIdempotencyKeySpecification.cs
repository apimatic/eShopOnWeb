using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendNotificationByIdempotencyKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
