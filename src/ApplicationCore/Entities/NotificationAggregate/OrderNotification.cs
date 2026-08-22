using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        int? contactNumberId,
        DateTimeOffset? scheduledFor = null,
        int? parentNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.Negative(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        ScheduledFor = scheduledFor;
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        DeliveryStatus = "pending";
        BodyRedacted = false;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool BodyRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string DeliveryStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? ContactNumberId { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }

    public void RecordSendResult(string? providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrWhiteSpace(status) ? "send_failed" : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            DeliveryStatus = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastProviderSyncAt = DateTimeOffset.UtcNow;

        if (BodyRedacted)
        {
            return;
        }

        if (body == string.Empty)
        {
            MarkBodyRedacted();
        }
        else if (!string.IsNullOrEmpty(body))
        {
            Body = body;
        }
    }

    public void MarkBodyRedacted()
    {
        Body = null;
        BodyRedacted = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(DeliveryStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "sent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInFlight()
    {
        return string.Equals(DeliveryStatus, "queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "accepted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "sending", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "pending", StringComparison.OrdinalIgnoreCase);
    }

    public bool DidNotReachShopper()
    {
        return string.Equals(DeliveryStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "send_failed", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(ProviderMessageSid);
    }

    public bool IsTerminal()
    {
        return string.Equals(DeliveryStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "canceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeliveryStatus, "send_failed", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsScheduledFollowUp()
    {
        return Kind == NotificationKind.DeliveryFollowUp
            && string.Equals(DeliveryStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(ProviderMessageSid);
    }
}
