using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.OriginalNotificationId == originalNotificationId
            && n.IdempotencyKey == idempotencyKey);
    }
}
