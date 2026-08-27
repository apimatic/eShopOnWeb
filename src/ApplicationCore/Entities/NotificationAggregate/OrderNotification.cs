using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS notification attempt for an order: what was sent,
/// the provider's identifier for it, and what became of it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationType type,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Status = NotificationStatuses.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
        LastUpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message; null if the send was rejected before one was assigned.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its wire status), or a local marker such as "send-failed".</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>For provider-scheduled messages, when the provider will send it.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>If this record is an operator resend, the notification it resends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator resends.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUpdatedAt { get; private set; }

    public bool ContentDisposed => Body is null;

    public void MarkProviderAccepted(string providerMessageSid, string? status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatuses.Accepted : status;
        Touch();
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = NotificationStatuses.SendFailed;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void UpdateProviderStatus(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void MarkContentDisposed()
    {
        Body = null;
        Touch();
    }

    private void Touch() => LastUpdatedAt = DateTimeOffset.UtcNow;
}
