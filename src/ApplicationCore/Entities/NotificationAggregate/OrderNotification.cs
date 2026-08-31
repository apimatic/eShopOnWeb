using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS notification sent (or attempted) for an order, tracking the provider's
/// message identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        string toNumber,
        NotificationType type,
        string body,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public string? MessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void MarkSubmitted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
    }

    public void MarkFailed(string status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void UpdateStatus(string status, int? errorCode, string? errorMessage)
    {
        // Never regress a terminal status with a stale earlier one
        if (NotificationStatus.IsTerminal(Status) && !NotificationStatus.IsTerminal(status))
        {
            return;
        }
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}

public static class NotificationStatus
{
    public const string Pending = "pending";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    public static bool IsTerminal(string? status) =>
        status is Delivered or Undelivered or Failed or Canceled;

    public static bool IsResendable(string? status) =>
        status is Undelivered or Failed;
}
