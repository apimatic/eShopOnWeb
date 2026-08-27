using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class NotificationResendRequest : BaseEntity, IAggregateRoot
{
    private NotificationResendRequest() { }

    public NotificationResendRequest(
        int sourceNotificationId,
        string idempotencyKey,
        int resultNotificationId,
        DateTimeOffset createdAt)
    {
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultNotificationId = resultNotificationId;
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
