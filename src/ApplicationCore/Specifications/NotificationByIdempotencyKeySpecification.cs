using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A resend that was already produced under a given idempotency key, so a repeat of the same request
/// returns it instead of sending a second message.
/// </summary>
public class NotificationByIdempotencyKeySpecification : Specification<SmsNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
