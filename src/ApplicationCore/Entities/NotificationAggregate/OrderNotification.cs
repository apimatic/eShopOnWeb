using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        ProviderStatus = NotificationDeliveryStatus.PendingProviderRequest;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? LastProviderCheckAt { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void RecordProviderState(ProviderMessage message, DateTimeOffset checkedAt)
    {
        ProviderMessageSid = message.Sid;
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderCreatedAt = message.CreatedAt ?? ProviderCreatedAt;
        ProviderSentAt = message.SentAt ?? ProviderSentAt;
        LastProviderCheckAt = checkedAt;

        if (string.Equals(message.Status, NotificationDeliveryStatus.Canceled, StringComparison.OrdinalIgnoreCase))
        {
            CancellationRequestedAt = null;
        }
    }

    public void RecordProviderRequestFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        ProviderStatus = NotificationDeliveryStatus.ProviderRequestFailed;
        ProviderErrorCode = errorCode;
        LastProviderCheckAt = checkedAt;
    }

    public void RequestCancellation(DateTimeOffset requestedAt)
    {
        CancellationRequestedAt ??= requestedAt;
    }

    public void ClearCancellationRequest()
    {
        CancellationRequestedAt = null;
    }

    public void Redact(DateTimeOffset redactedAt)
    {
        Body = null;
        ContentRedactedAt ??= redactedAt;
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
    public const string PendingProviderRequest = "pending-provider-request";
    public const string ProviderRequestFailed = "provider-request-failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Failed = "failed";
    public const string Undelivered = "undelivered";
}
