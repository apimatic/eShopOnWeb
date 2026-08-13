using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the notification a resend already produced for a given source notification under a given
/// idempotency key — used to make a repeated resend request not send a second message.
/// </summary>
public class NotificationResendSpecification : Specification<OrderNotification>
{
    public NotificationResendSpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == sourceNotificationId
            && n.IdempotencyKey == idempotencyKey);
    }
}
