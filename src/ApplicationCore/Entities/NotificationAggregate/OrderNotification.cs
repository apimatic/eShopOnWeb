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
        NotificationKind kind,
        string destinationNumber,
        string body,
        string? providerMessageSid,
        string? providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? providerDateSent,
        DateTimeOffset? scheduledFor,
        string? sendFailureReason,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ProviderDateSent = providerDateSent;
        ScheduledFor = scheduledFor;
        SendFailureReason = sendFailureReason;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ContentRedaction = "present";
        CreatedAt = DateTimeOffset.UtcNow;
        LastProviderSyncAt = providerMessageSid is null ? null : DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ContentRedaction { get; private set; }
    public bool ContentDisposed => string.Equals(ContentRedaction, "disposed", StringComparison.Ordinal);
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? SendFailureReason { get; private set; }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTerminalProviderStatus()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return ProviderMessageSid is null;
        }

        return ProviderStatus.Equals("delivered", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("received", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("read", StringComparison.OrdinalIgnoreCase);
    }

    public bool DidNotReachShopper()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return ProviderMessageSid is null;
        }

        return ProviderStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("canceled", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsScheduledOutstanding()
    {
        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase);
    }

    public void ApplyProviderState(
        string? status,
        string? body,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateSent)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
        LastProviderSyncAt = DateTimeOffset.UtcNow;

        if (ContentDisposed)
        {
            Body = null;
            return;
        }

        if (body is not null)
        {
            Body = string.IsNullOrWhiteSpace(body) ? null : body;
        }
    }

    public void MarkContentDisposed()
    {
        ContentRedaction = "disposed";
        Body = null;
    }

    public void MarkSendFailed(string reason)
    {
        SendFailureReason = reason;
        ProviderStatus = "failed";
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public string? RetrievableBody() => ContentDisposed ? null : Body;
}
