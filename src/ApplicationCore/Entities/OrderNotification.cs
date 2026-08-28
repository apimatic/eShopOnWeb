using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationKind kind, string body, DateTimeOffset createdAt, int? sourceNotificationId = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        SourceNotificationId = sourceNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public DateTimeOffset? LastSyncFailedAt { get; private set; }

    public void ApplyProviderSnapshot(ProviderMessageSnapshot snapshot, DateTimeOffset now)
    {
        ProviderMessageSid = snapshot.Sid;
        ProviderStatus = snapshot.Status ?? ProviderStatus;
        ProviderErrorCode = snapshot.ErrorCode;
        ProviderErrorMessage = snapshot.ErrorMessage;
        ProviderDateCreated = snapshot.DateCreated;
        ProviderDateSent = snapshot.DateSent;
        UpdatedAt = now;
        LastSyncFailedAt = null;
    }

    public void MarkProviderFailure(DateTimeOffset now, int? statusCode = null)
    {
        ProviderStatus = "provider_error";
        ProviderErrorCode = statusCode;
        ProviderErrorMessage = null;
        UpdatedAt = now;
        LastSyncFailedAt = now;
    }

    public void SetScheduledFor(DateTimeOffset sendAt) => ScheduledFor = sendAt;

    public void RequestCancellation(DateTimeOffset now)
    {
        CancellationRequestedAt ??= now;
        UpdatedAt = now;
    }

    public void MarkSyncFailure(DateTimeOffset now)
    {
        LastSyncFailedAt = now;
        UpdatedAt = now;
    }

    public void DisposeContent(DateTimeOffset now)
    {
        Body = null;
        ContentDisposedAt = now;
        UpdatedAt = now;
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

public sealed record ProviderMessageSnapshot(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? Body);

public sealed record ProviderMessageRecord(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
