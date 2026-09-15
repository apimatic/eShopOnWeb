using System;
using System.Globalization;
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
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "not_attempted";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public string? ProviderFrom { get; private set; }
    public string? ProviderMessagingServiceSid { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public string? ProviderDateUpdated { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AttemptedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderResult(ProviderMessageState state, DateTimeOffset now)
    {
        ProviderMessageSid = state.Sid ?? ProviderMessageSid;
        ProviderStatus = state.Status ?? ProviderStatus;
        ProviderFrom = state.From ?? ProviderFrom;
        ProviderMessagingServiceSid = state.MessagingServiceSid ?? ProviderMessagingServiceSid;
        ProviderDateCreated = state.DateCreated ?? ProviderDateCreated;
        ProviderDateSent = state.DateSent ?? ProviderDateSent;
        if (DateTimeOffset.TryParse(state.DateSent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sentAt))
        {
            ProviderSentAt = sentAt;
        }
        ProviderDateUpdated = state.DateUpdated ?? ProviderDateUpdated;
        ProviderErrorCode = state.ErrorCode;
        ProviderErrorMessage = state.ErrorMessage;
        AttemptedAt ??= now;
    }

    public void RecordFailure(string outcome, string safeMessage, DateTimeOffset now)
    {
        ProviderStatus = outcome;
        ProviderErrorMessage = safeMessage;
        AttemptedAt ??= now;
    }

    public void DisposeContent(DateTimeOffset now)
    {
        Body = null;
        ContentDisposedAt = now;
    }
}

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
