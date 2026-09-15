using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds the message a prior resend produced under a given idempotency key, so repeating a request
/// under the same key returns that message instead of sending a second one.
/// </summary>
public class NotificationByResendKeySpecification : Specification<Notification>
{
    public NotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
