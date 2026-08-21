using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public ResendByIdempotencySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}
