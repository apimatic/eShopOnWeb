using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendBySourceAndKeySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public ResendBySourceAndKeySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId && n.ResendIdempotencyKey == idempotencyKey);
    }
}
