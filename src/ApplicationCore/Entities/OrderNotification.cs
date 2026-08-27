using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Kind = kind;
        Content = Guard.Against.NullOrWhiteSpace(content, nameof(content));
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public Order? Order { get; private set; }
    public int? ContactNumberId { get; private set; }
    public ContactNumber? ContactNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastSynchronizedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(
        string messageSid,
        string status,
        int? errorCode,
        DateTimeOffset? providerDateCreated,
        DateTimeOffset? providerDateSent,
        DateTimeOffset synchronizedAt)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(messageSid, nameof(messageSid));
        ApplyProviderState(status, errorCode, providerDateCreated, providerDateSent, synchronizedAt);
    }

    public void RefreshProviderState(
        string status,
        int? errorCode,
        DateTimeOffset? providerDateCreated,
        DateTimeOffset? providerDateSent,
        DateTimeOffset synchronizedAt)
    {
        ApplyProviderState(status, errorCode, providerDateCreated, providerDateSent, synchronizedAt);
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset synchronizedAt)
    {
        ProviderStatus = "provider_rejected";
        ProviderErrorCode = errorCode;
        LastSynchronizedAt = synchronizedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Content = null;
        ContentDisposedAt = disposedAt;
    }

    private void ApplyProviderState(
        string status,
        int? errorCode,
        DateTimeOffset? providerDateCreated,
        DateTimeOffset? providerDateSent,
        DateTimeOffset synchronizedAt)
    {
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        ProviderErrorCode = errorCode;
        ProviderDateCreated = providerDateCreated ?? ProviderDateCreated;
        ProviderDateSent = providerDateSent ?? ProviderDateSent;
        LastSynchronizedAt = synchronizedAt;
    }
}
