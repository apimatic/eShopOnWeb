using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The message a prior resend produced under a given idempotency key, if any.</summary>
public class SmsNotificationByIdempotencyKeySpecification : Specification<SmsNotification>
{
    public SmsNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
