using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationType type,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }
    public bool CancellationPending { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(TwilioMessageState state, DateTimeOffset checkedAt)
    {
        ProviderMessageSid = state.Sid;
        ProviderStatus = state.Status;
        ProviderErrorCode = state.ErrorCode;
        ProviderDateCreated = state.DateCreated;
        ProviderDateSent = state.DateSent;
        LastCheckedAt = checkedAt;
        if (state.Status == "canceled")
        {
            CancellationPending = false;
        }
    }

    public void RecordFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        LastCheckedAt = checkedAt;
    }

    public void RequestCancellation()
    {
        CancellationPending = ProviderMessageSid is not null && ProviderStatus == "scheduled";
    }

    public void RecordCancellation(TwilioMessageState state, DateTimeOffset checkedAt)
    {
        RecordProviderState(state, checkedAt);
        CancellationPending = false;
    }

    public void Redact(DateTimeOffset redactedAt)
    {
        Body = null;
        ContentRedactedAt = redactedAt;
    }
}

public enum NotificationType
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}
