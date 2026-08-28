using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Kind = kind;
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderMessage(ProviderMessage message, DateTimeOffset checkedAt)
    {
        ProviderMessageSid = message.Sid;
        RefreshProviderState(message, checkedAt);
    }

    public void RefreshProviderState(ProviderMessage message, DateTimeOffset checkedAt)
    {
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderErrorMessage = message.ErrorMessage;
        ProviderDateCreated = message.DateCreated;
        ProviderDateSent = message.DateSent;
        LastCheckedAt = checkedAt;
    }

    public void RecordProviderFailure(int? errorCode, string? errorMessage, DateTimeOffset checkedAt)
    {
        ProviderStatus = "provider-rejected";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastCheckedAt = checkedAt;
    }

    public void Redact()
    {
        Body = null;
        ContentRedacted = true;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}
