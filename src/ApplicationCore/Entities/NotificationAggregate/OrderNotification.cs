using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        LocalOutcome = NotificationLocalOutcome.Pending;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? ProviderFrom { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public string? ProviderDateUpdated { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public NotificationLocalOutcome LocalOutcome { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool CancellationPending { get; private set; }

    public void RecordProviderState(
        string providerMessageId,
        string? status,
        string? providerFrom,
        int? errorCode,
        string? errorMessage,
        string? dateCreated,
        string? dateUpdated,
        string? dateSent,
        DateTimeOffset syncedAt)
    {
        ProviderMessageId = providerMessageId;
        ProviderStatus = status;
        ProviderFrom = providerFrom;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateCreated = dateCreated;
        ProviderDateUpdated = dateUpdated;
        ProviderDateSent = dateSent;
        LastProviderSyncAt = syncedAt;
        LocalOutcome = NotificationLocalOutcome.AcceptedByProvider;
        CancellationPending = false;
    }

    public void RecordProviderFailure(int? statusCode, DateTimeOffset attemptedAt)
    {
        ProviderStatus = "provider-call-failed";
        ProviderErrorCode = statusCode;
        ProviderErrorMessage = null;
        LastProviderSyncAt = attemptedAt;
        LocalOutcome = NotificationLocalOutcome.ProviderCallFailed;
    }

    public void MarkCancellationPending() => CancellationPending = true;

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Content = null;
        ContentDisposedAt = disposedAt;
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

public enum NotificationLocalOutcome
{
    Pending,
    AcceptedByProvider,
    ProviderCallFailed
}
