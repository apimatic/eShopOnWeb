using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destination,
        string body,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destination, nameof(destination));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Destination { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? SendFailure { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void MarkAsResendOf(int sourceNotificationId)
    {
        SourceNotificationId = sourceNotificationId;
        Kind = NotificationKind.Resend;
    }

    public void ApplyProviderResult(string? sid, string? status, int? errorCode)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            ProviderSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        SendFailure = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string reason)
    {
        SendFailure = reason;
        if (string.IsNullOrWhiteSpace(ProviderStatus))
        {
            ProviderStatus = "failed";
        }
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        if (string.IsNullOrWhiteSpace(ProviderSid))
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered";
    }

    public bool IsPendingWithProvider()
    {
        return ProviderStatus is "scheduled" or "queued" or "accepted" or "sending";
    }
}
