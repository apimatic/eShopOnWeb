using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string PendingLocalStatus = "pending";
    public const string SendFailedStatus = "send_failed";

#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destinationE164,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        int? resentFromNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationE164, nameof(destinationE164));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationE164 = destinationE164;
        Body = body;
        ProviderStatus = PendingLocalStatus;
        CreatedAt = DateTimeOffset.UtcNow;
        ScheduledSendAt = scheduledSendAt;
        ResentFromNotificationId = resentFromNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationE164 { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }
    public string? SendFailure { get; private set; }

    public bool HasReachedShopper =>
        string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);

    public bool IsTerminalProviderStatus =>
        string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, SendFailedStatus, StringComparison.OrdinalIgnoreCase);

    public bool CanResend =>
        !HasReachedShopper
        && (string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, SendFailedStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, PendingLocalStatus, StringComparison.OrdinalIgnoreCase));

    public bool IsScheduledFollowUp =>
        Kind == NotificationKind.DeliveryFollowUp
        && (string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, PendingLocalStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase));

    public void RecordProviderAcceptance(string sid, string status)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        LastSyncedAt = DateTimeOffset.UtcNow;
        SendFailure = null;
    }

    public void RecordSendFailure(string reason)
    {
        ProviderStatus = SendFailedStatus;
        SendFailure = reason;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }
}
