using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(int sourceNotificationId, string resendIdempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId
            && n.ResendIdempotencyKey == resendIdempotencyKey);
    }
}
