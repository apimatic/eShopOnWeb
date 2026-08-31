using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

public class ResendResult
{
    public ResendResult(OrderNotification notification, bool alreadyExisted)
    {
        Notification = notification;
        AlreadyExisted = alreadyExisted;
    }

    /// <summary>The notification the resend produced (or the one a previous call with the same key produced).</summary>
    public OrderNotification Notification { get; }

    /// <summary>True when the idempotency key was already used and no new message was sent.</summary>
    public bool AlreadyExisted { get; }
}
