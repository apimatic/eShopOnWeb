using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByResendIdempotencySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n =>
            n.ResentFromNotificationId == originalNotificationId &&
            n.IdempotencyKey == idempotencyKey);
    }
}
