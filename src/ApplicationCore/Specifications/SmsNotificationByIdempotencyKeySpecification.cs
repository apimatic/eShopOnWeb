using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notification (if any) already produced under a given resend idempotency key.
/// Lets a repeated resend under the same key return the first result without sending again.
/// </summary>
public sealed class SmsNotificationByIdempotencyKeySpecification : Specification<SmsNotification>
{
    public SmsNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
