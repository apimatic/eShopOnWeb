using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop raised about an order. It carries enough of the state the provider
/// owns — the provider's message identifier and current delivery outcome — that a later request can
/// act on it (resend, cancel, redact) and report on it, independently of the request that sent it.
///
/// The <see cref="ToPhoneNumber"/> is stored (it is the data) but must never be written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owner of the order this notification is about; used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination in E.164, or null when the shopper had no number on file.</summary>
    public string? ToPhoneNumber { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio message SID). Null until accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's raw status string, kept verbatim for fidelity/reporting.</summary>
    public string? ProviderStatusRaw { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True for the delivery follow-up that was queued with the provider for later.</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>True once the message body has been redacted at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key of the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one was a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationType type, string? toPhoneNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Status = NotificationStatus.NoContactNumber;
    }

    /// <summary>Records that this notification is the result of an operator resend.</summary>
    public void MarkAsResend(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    /// <summary>The shopper had no number on file, so nothing was sent.</summary>
    public void RecordNoContactNumber()
    {
        Status = NotificationStatus.NoContactNumber;
        Touch();
    }

    /// <summary>The provider accepted the send (immediate or scheduled).</summary>
    public void RecordAccepted(string providerMessageSid, string? providerStatusRaw, bool scheduled,
        DateTimeOffset? scheduledFor, DateTimeOffset? providerDateSent)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));

        ProviderMessageSid = providerMessageSid;
        ProviderStatusRaw = providerStatusRaw;
        IsScheduled = scheduled;
        ScheduledFor = scheduledFor;
        ProviderDateSent = providerDateSent;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
        Status = MapStatus(providerStatusRaw, scheduled);
        Touch();
    }

    /// <summary>The message could not be handed to the provider at all. The order still succeeds.</summary>
    public void RecordSendFailed(string? reason)
    {
        Status = NotificationStatus.SendFailed;
        ProviderErrorMessage = reason;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryState(string? providerStatusRaw, int? errorCode, string? errorMessage,
        DateTimeOffset? providerDateSent)
    {
        ProviderStatusRaw = providerStatusRaw;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (providerDateSent.HasValue)
        {
            ProviderDateSent = providerDateSent;
        }

        Status = MapStatus(providerStatusRaw, IsScheduled && providerStatusRaw is null);
        Touch();
    }

    /// <summary>The scheduled message was called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        ProviderStatusRaw = "canceled";
        Touch();
    }

    /// <summary>The message body has been redacted at the provider; the record survives.</summary>
    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>Maps a Twilio message status string onto <see cref="NotificationStatus"/>.</summary>
    public static NotificationStatus MapStatus(string? providerStatusRaw, bool scheduledHint)
    {
        if (string.IsNullOrWhiteSpace(providerStatusRaw))
        {
            return scheduledHint ? NotificationStatus.Scheduled : NotificationStatus.Queued;
        }

        return providerStatusRaw.Trim().ToLowerInvariant() switch
        {
            "queued" => NotificationStatus.Queued,
            "accepted" => NotificationStatus.Queued,
            "scheduled" => NotificationStatus.Scheduled,
            "sending" => NotificationStatus.Sending,
            "sent" => NotificationStatus.Sent,
            "delivered" => NotificationStatus.Delivered,
            "read" => NotificationStatus.Delivered,
            "receiving" => NotificationStatus.Sending,
            "received" => NotificationStatus.Delivered,
            "undelivered" => NotificationStatus.Undelivered,
            "failed" => NotificationStatus.Failed,
            "canceled" => NotificationStatus.Canceled,
            "cancelled" => NotificationStatus.Canceled,
            "partially_delivered" => NotificationStatus.Delivered,
            _ => NotificationStatus.Unknown
        };
    }

    /// <summary>Whether the delivery outcome is settled and need not be re-fetched from the provider.</summary>
    public bool IsTerminal() => Status is NotificationStatus.Delivered or NotificationStatus.Undelivered
        or NotificationStatus.Failed or NotificationStatus.Canceled or NotificationStatus.NoContactNumber
        or NotificationStatus.SendFailed;
}
