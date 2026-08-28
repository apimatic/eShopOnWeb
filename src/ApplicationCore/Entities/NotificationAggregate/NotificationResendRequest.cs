using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendRequest : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private NotificationResendRequest() { }
#pragma warning restore CS8618

    public NotificationResendRequest(int sourceNotificationId, string idempotencyKey,
        OrderNotification resultNotification, DateTimeOffset createdAt)
    {
        SourceNotificationId = Guard.Against.NegativeOrZero(sourceNotificationId);
        IdempotencyKey = Guard.Against.NullOrWhiteSpace(idempotencyKey);
        ResultNotification = Guard.Against.Null(resultNotification);
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }
    public OrderNotification ResultNotification { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
