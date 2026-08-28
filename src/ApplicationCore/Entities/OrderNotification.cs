using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string destination,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        ContactNumberId = contactNumberId;
        Destination = Guard.Against.NullOrWhiteSpace(destination, nameof(destination));
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int ContactNumberId { get; private set; }
    public string Destination { get; private set; } = null!;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastStatusCheckedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public bool IsScheduledFollowUp => Kind == NotificationKind.DeliveryFollowUp && ScheduledFor.HasValue;
    public bool IsContentDisposed => ContentDisposedAt.HasValue;

    public void RecordProviderState(string sid, string status, int? errorCode, string? errorMessage,
        DateTimeOffset? dateSent)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(sid, nameof(sid));
        UpdateProviderState(status, errorCode, errorMessage, dateSent);
    }

    public void UpdateProviderState(string status, int? errorCode, string? errorMessage,
        DateTimeOffset? dateSent)
    {
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    public void RecordSendFailure(int? errorCode, string? errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposedAt = DateTimeOffset.UtcNow;
    }
}
