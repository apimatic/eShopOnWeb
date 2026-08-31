using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS notification sent (or attempted) for an order, carrying the
/// provider-owned state (message SID, delivery status, error detail) so later
/// requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local status used when the provider never accepted the message.
    public const string SendFailedStatus = "send-failed";
    // Local status before the provider call has completed.
    public const string PendingStatus = "pending";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = PendingStatus;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>The provider's wire status (queued/sent/delivered/undelivered/failed/scheduled/canceled)
    /// or a local marker (pending/send-failed).</summary>
    public string Status { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message; null if the send never reached the provider.</summary>
    public string? MessageSid { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public bool ContentRedacted { get; private set; }

    public void MarkAccepted(string messageSid, string? status)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = status ?? PendingStatus;
    }

    public void MarkSendFailed(int? errorCode, string? errorMessage)
    {
        Status = SendFailedStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void UpdateStatus(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        ErrorCode = errorCode ?? ErrorCode;
        ErrorMessage = errorMessage ?? ErrorMessage;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }
}
