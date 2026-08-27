using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string buyerId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        ProviderStatus = NotificationDeliveryStatus.Pending;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = NotificationDeliveryStatus.Pending;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public void RecordProviderState(ProviderMessageState message, DateTimeOffset updatedAt)
    {
        ProviderMessageSid = message.Sid;
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderCreatedAt = message.DateCreated;
        ProviderSentAt = message.DateSent;
        UpdatedAt = updatedAt;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = NotificationDeliveryStatus.ProviderRequestFailed;
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void RecordCancellationFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = "cancellation_failed";
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void Redact(DateTimeOffset redactedAt)
    {
        Content = null;
        ContentRedactedAt ??= redactedAt;
        UpdatedAt = redactedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}

public static class NotificationDeliveryStatus
{
    public const string Pending = "pending";
    public const string ProviderRequestFailed = "provider_request_failed";

    public static bool DidNotReachShopper(string status) =>
        status.Equals(ProviderRequestFailed, StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase);

    public static bool IsScheduled(string status) =>
        status.Equals("scheduled", StringComparison.OrdinalIgnoreCase);
}

public sealed record ProviderMessageState(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
