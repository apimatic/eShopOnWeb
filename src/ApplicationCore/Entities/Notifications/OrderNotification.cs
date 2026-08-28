using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationType type,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Type = type;
        Content = content;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ContentDeletedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public bool IsResendable => Content is not null &&
        ProviderStatus is "failed" or "undelivered" or "partially_delivered" or "send_failed";

    public bool NeedsCancellation => ScheduledFor is not null &&
        ProviderMessageSid is not null &&
        ProviderStatus is not ("canceled" or "delivered" or "failed" or "undelivered" or "sent" or "read");

    public void ApplyProviderState(
        string sid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent)
    {
        ProviderMessageSid = sid;
        var cancellationIsStillPending = CancellationRequestedAt is not null &&
            ProviderStatus == "cancel_pending" &&
            status is "accepted" or "scheduled" or "queued" or "sending";
        if (!cancellationIsStillPending)
        {
            ProviderStatus = status;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
    }

    public void MarkProviderFailure(int? errorCode, string errorMessage)
    {
        ProviderStatus = "send_failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RequestCancellation(DateTimeOffset requestedAt)
    {
        CancellationRequestedAt = requestedAt;
        if (ProviderStatus != "canceled")
        {
            ProviderStatus = "cancel_pending";
        }
    }

    public void MarkCancellationFailure(int? errorCode)
    {
        ProviderStatus = "cancel_pending";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = "Provider cancellation is pending retry.";
    }

    public void MarkContentDeleted(DateTimeOffset deletedAt)
    {
        Content = null;
        ContentDeletedAt = deletedAt;
    }
}
