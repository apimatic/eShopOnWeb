using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendNotificationByKeySpecification : Specification<OrderNotification>
{
    public ResendNotificationByKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.OriginalNotificationId == originalNotificationId
                         && n.ResendIdempotencyKey == idempotencyKey);
    }
}
