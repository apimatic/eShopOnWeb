using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        int? originalNotificationId = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        CreatedAt = createdAt;
        OriginalNotificationId = originalNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public string? ProviderDirection { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public string? ProviderDateUpdated { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public DateTimeOffset? LastRefreshFailedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? CancellationFailedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }

    public bool IsContentDisposed => ContentDisposedAt.HasValue;

    public void ApplyProviderState(
        string? sid,
        string? status,
        string? direction,
        int? errorCode,
        string? errorMessage,
        string? dateCreated,
        string? dateSent,
        string? dateUpdated,
        DateTimeOffset refreshedAt,
        DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = sid ?? ProviderMessageSid;
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? ProviderStatus : status;
        ProviderDirection = direction;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        ProviderDateUpdated = dateUpdated;
        LastRefreshedAt = refreshedAt;
        LastRefreshFailedAt = null;
        if (string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            CancellationFailedAt = null;
        }
        ScheduledFor = scheduledFor ?? ScheduledFor;
    }

    public void MarkProviderFailure(int? errorCode, string safeMessage, DateTimeOffset occurredAt)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = safeMessage;
        LastRefreshFailedAt = occurredAt;
    }

    public void MarkRefreshFailure(DateTimeOffset occurredAt)
    {
        LastRefreshFailedAt = occurredAt;
    }

    public void MarkCancellationRequested(DateTimeOffset occurredAt)
    {
        CancellationRequestedAt = occurredAt;
    }

    public void MarkCancellationFailure(int? errorCode, string safeMessage, DateTimeOffset occurredAt)
    {
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = safeMessage;
        CancellationFailedAt = occurredAt;
    }

    public void MarkContentDisposed(DateTimeOffset occurredAt)
    {
        Body = null;
        ContentDisposedAt = occurredAt;
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
