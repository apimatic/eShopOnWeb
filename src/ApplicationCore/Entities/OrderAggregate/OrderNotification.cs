using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        int? originalNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateUpdated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public bool ProviderStateStale { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(ProviderMessageState state, DateTimeOffset syncedAt)
    {
        ProviderMessageSid = state.Sid ?? ProviderMessageSid;
        ProviderStatus = state.Status;
        ProviderErrorCode = state.ErrorCode;
        ProviderErrorMessage = state.ErrorMessage;
        ScheduledFor = state.ScheduledFor ?? ScheduledFor;
        ProviderDateCreated = state.DateCreated ?? ProviderDateCreated;
        ProviderDateUpdated = state.DateUpdated ?? ProviderDateUpdated;
        ProviderDateSent = state.DateSent ?? ProviderDateSent;
        UpdatedAt = syncedAt;
        LastProviderSyncAt = syncedAt;
        ProviderStateStale = false;
    }

    public void RecordProviderFailure(string status, string safeMessage, DateTimeOffset at)
    {
        ProviderStatus = status;
        ProviderErrorMessage = safeMessage;
        UpdatedAt = at;
        LastProviderSyncAt = at;
        ProviderStateStale = false;
    }

    public void MarkProviderStateStale(DateTimeOffset at)
    {
        ProviderStateStale = true;
        UpdatedAt = at;
    }

    public void DisposeContent(DateTimeOffset at)
    {
        Body = null;
        ContentDisposedAt = at;
        UpdatedAt = at;
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

public sealed record ProviderMessageState(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateUpdated,
    DateTimeOffset? DateSent,
    DateTimeOffset? ScheduledFor = null,
    string? Body = null);

public sealed record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessageRecord(
    string Sid,
    string Status,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateUpdated,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
