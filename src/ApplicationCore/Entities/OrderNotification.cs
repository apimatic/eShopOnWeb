using System;
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
        int? originalNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        ProviderStatus = NotificationProviderStatus.PendingSubmission;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastProviderCheckAt { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void RecordProviderAcceptance(string sid, string status, int? errorCode, DateTimeOffset? dateSent, DateTimeOffset checkedAt)
    {
        ProviderMessageSid = sid;
        ApplyProviderState(status, errorCode, dateSent, checkedAt);
    }

    public void RecordSubmissionFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        ProviderStatus = NotificationProviderStatus.SubmissionFailed;
        ProviderErrorCode = errorCode;
        LastProviderCheckAt = checkedAt;
    }

    public void ApplyProviderState(string status, int? errorCode, DateTimeOffset? dateSent, DateTimeOffset checkedAt)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        LastProviderCheckAt = checkedAt;
    }

    public void MarkCancellationPending(DateTimeOffset checkedAt)
    {
        ProviderStatus = NotificationProviderStatus.CancellationPending;
        LastProviderCheckAt = checkedAt;
    }

    public void Redact(DateTimeOffset redactedAt)
    {
        Body = null;
        ContentRedactedAt = redactedAt;
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

public static class NotificationProviderStatus
{
    public const string PendingSubmission = "pending-submission";
    public const string SubmissionFailed = "submission-failed";
    public const string CancellationPending = "cancellation-pending";
}
