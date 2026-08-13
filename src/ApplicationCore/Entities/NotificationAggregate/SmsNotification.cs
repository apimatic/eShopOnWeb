using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS message raised for an order as it moved through its lifecycle.
/// It carries enough of the state the provider owns — its message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="Status"/>) — that a
/// later request can act on it (fetch, cancel, redact, re-send) and report on it, not only the
/// request that first sent it.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper this message is about. Notifications are scoped to their owner.</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The destination number (provider-canonical E.164). PII — never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>
    /// The message text. Held so it can be re-sent and shown to operators; cleared when the
    /// content is disposed of.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message, once it has accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The current delivery outcome. See <see cref="SmsDeliveryStatus"/>.</summary>
    public string Status { get; private set; } = SmsDeliveryStatus.Pending;

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>True for the follow-up message queued with the provider to go out days later.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key when this notification was produced by a re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is a re-send, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }
#pragma warning restore CS8618

    public SmsNotification(string buyerId, int orderId, NotificationType type, string toPhoneNumber, string body,
        bool isScheduled = false, string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        BuyerId = buyerId;
        OrderId = orderId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        IsScheduled = isScheduled;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    /// <summary>The provider accepted the message; capture its identifier and initial status.</summary>
    public void RecordProviderAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? SmsDeliveryStatus.Accepted : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The provider could not be handed the message at all; the operation still succeeds.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        Status = SmsDeliveryStatus.SendFailed;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from the provider's latest record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (string.IsNullOrEmpty(status)) return;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The (scheduled) message was called off at the provider before it went out.</summary>
    public void MarkCanceled()
    {
        Status = SmsDeliveryStatus.Canceled;
        Touch();
    }

    /// <summary>The message content has been disposed of; keep the fact and outcome, drop the text.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
