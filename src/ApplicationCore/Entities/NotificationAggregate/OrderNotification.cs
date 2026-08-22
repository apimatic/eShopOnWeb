using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        int? parentNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        ParentNotificationId = parentNotificationId;
        ContentRedacted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void RecordProviderState(
        string? messageSid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (!string.IsNullOrEmpty(messageSid))
        {
            ProviderMessageSid = messageSid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsTerminalStatus()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";
    }

    public bool IsPendingSend()
    {
        return ProviderStatus is "pending" or "accepted" or "queued" or "scheduled" or "sending";
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" || string.IsNullOrEmpty(ProviderMessageSid);
    }
}
