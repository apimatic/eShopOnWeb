using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
