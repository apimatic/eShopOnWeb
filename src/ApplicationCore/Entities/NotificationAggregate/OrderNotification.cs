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
        OrderNotificationKind kind,
        string body,
        string destinationNumber,
        int? contactNumberId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        CreatedUtc = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }

    /// <summary>
    /// Local copy of the SMS body. Cleared when the shopper asks for disposal.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>
    /// Canonical destination. Never write this to logs.
    /// </summary>
    public string DestinationNumber { get; private set; }

    public int? ContactNumberId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>
    /// When this row is a resend, the notification that was retried.
    /// </summary>
    public int? SourceNotificationId { get; private set; }

    /// <summary>
    /// Caller-supplied key that makes a resend idempotent.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
        ProviderStatus = "scheduled";
    }

    public void ApplyProviderResult(string? messageSid, string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(messageSid))
        {
            ProviderMessageSid = messageSid;
        }

        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkSendFailed(string reason)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = reason;
    }

    public void AttachResend(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        Kind = OrderNotificationKind.Resend;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsStillScheduled()
    {
        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
    }
}
