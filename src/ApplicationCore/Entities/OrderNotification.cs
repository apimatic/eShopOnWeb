using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = NotificationDeliveryStatus.Pending;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = NotificationDeliveryStatus.Pending;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderResult(string sid, string status, int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(sid);
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status);
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void RecordProviderStatus(string status, int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status);
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void RecordFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = NotificationDeliveryStatus.Failed;
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void Redact(DateTimeOffset updatedAt)
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = updatedAt;
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

public static class NotificationDeliveryStatus
{
    public const string Pending = "pending";
    public const string Failed = "failed";
}
