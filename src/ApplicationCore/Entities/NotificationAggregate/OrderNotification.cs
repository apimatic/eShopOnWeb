using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string PendingStatus = "pending";
    public const string SkippedStatus = "skipped";
    public const string SendFailedStatus = "send_failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        int? contactNumberId,
        DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        ScheduledFor = scheduledFor;
        CreatedAt = DateTimeOffset.UtcNow;
        DeliveryStatus = PendingStatus;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string DeliveryStatus { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void MarkAsResendOf(int originalNotificationId)
    {
        ResentFromNotificationId = originalNotificationId;
        Kind = NotificationKind.Resend;
    }

    public void RecordProviderAcceptance(string sid, string status, DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = sid;
        DeliveryStatus = status;
        if (scheduledFor.HasValue)
        {
            ScheduledFor = scheduledFor;
        }
        LastSyncedAt = DateTimeOffset.UtcNow;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void RecordProviderFailure(string? errorCode, string? errorMessage)
    {
        DeliveryStatus = SendFailedStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string status, string? errorCode, string? errorMessage, string? body, DateTimeOffset? scheduledFor)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (scheduledFor.HasValue)
        {
            ScheduledFor = scheduledFor;
        }
        if (ContentRedacted)
        {
            Body = null;
        }
        else if (body != null)
        {
            Body = body;
            if (body.Length == 0)
            {
                ContentRedacted = true;
                Body = null;
            }
        }
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }

    public string? GetBodyForDisplay()
    {
        return ContentRedacted ? null : Body;
    }
}
