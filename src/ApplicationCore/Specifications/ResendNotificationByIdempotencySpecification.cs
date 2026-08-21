using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendNotificationByIdempotencySpecification : Specification<OrderNotification>
{
    public ResendNotificationByIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
