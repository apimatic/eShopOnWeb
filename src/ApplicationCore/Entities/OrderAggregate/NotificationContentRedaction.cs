using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class NotificationContentRedaction : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private NotificationContentRedaction() { }
#pragma warning restore CS8618

    public NotificationContentRedaction(int notificationId)
    {
        NotificationId = notificationId;
        RedactedAt = DateTimeOffset.UtcNow;
    }

    public int NotificationId { get; private set; }
    public DateTimeOffset RedactedAt { get; private set; }
}
