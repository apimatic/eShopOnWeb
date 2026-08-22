using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendIdempotencySpecification : Specification<OrderNotification>
{
    public ResendIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
