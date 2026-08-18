using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS raised for an order as it moved. It carries enough of the state
/// the provider owns — the provider's message identifier and its last-known delivery status —
/// that a later request (status refresh, cancel, resend, redact, reconcile) can act on it and
/// report on it, not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local sentinel status used when the provider never accepted the send at all.</summary>
    public const string SendErrorStatus = "send_error";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string ownerId, NotificationType type, string? toNumber, string? body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        OrderId = orderId;
        OwnerId = ownerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        CreatedDate = DateTimeOffset.UtcNow;
        UpdatedDate = CreatedDate;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity (username) of the shopper the order — and therefore this message — belongs to.</summary>
    public string OwnerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio message SID), once accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's last-known fine-grained status verbatim (e.g. delivered, undelivered, scheduled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Recipient number. Shopper data — persisted so a resend knows where to go, never written to logs.</summary>
    public string? ToNumber { get; private set; }

    /// <summary>The message text. Cleared here and redacted at the provider on content disposal.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>For a follow-up queued with the provider, when it is due to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this message, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>Coarse, provider-agnostic view of where the message got to.</summary>
    public NotificationDeliveryOutcome Outcome => Classify(ProviderStatus);

    /// <summary>Records that the provider accepted the message (immediate or scheduled).</summary>
    public void RecordAccepted(string providerSid, string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderMessageSid = providerSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
        Touch();
    }

    /// <summary>Records that the send could not even be handed to the provider.</summary>
    public void RecordSendError(string? errorMessage)
    {
        ProviderStatus = SendErrorStatus;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refreshes the stored status from the provider's current view of the message.</summary>
    public void UpdateProviderStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (providerStatus is null)
        {
            return;
        }

        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
        Touch();
    }

    /// <summary>Records that the message content has been disposed of (redacted at the provider too).</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;

    /// <summary>
    /// Maps a provider status string (Twilio message status) to a coarse delivery outcome.
    /// See https://www.twilio.com/docs/messaging/api/message-resource for the status set.
    /// </summary>
    public static NotificationDeliveryOutcome Classify(string? providerStatus)
    {
        if (string.IsNullOrEmpty(providerStatus))
        {
            return NotificationDeliveryOutcome.NotSent;
        }

        return providerStatus.ToLowerInvariant() switch
        {
            "delivered" or "read" => NotificationDeliveryOutcome.Reached,
            "undelivered" or "failed" => NotificationDeliveryOutcome.NotReached,
            "scheduled" => NotificationDeliveryOutcome.Scheduled,
            "canceled" or "cancelled" => NotificationDeliveryOutcome.Canceled,
            SendErrorStatus => NotificationDeliveryOutcome.SendError,
            "accepted" or "queued" or "sending" or "sent" => NotificationDeliveryOutcome.InFlight,
            _ => NotificationDeliveryOutcome.InFlight
        };
    }
}
