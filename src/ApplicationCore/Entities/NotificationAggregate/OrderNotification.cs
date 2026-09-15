using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single message that eShop attempted to send about an order, together with the provider-owned
/// state needed to act on it and report on it later: the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and its current delivery outcome (<see cref="DeliveryStatus"/>).
/// <para>
/// The record survives even when its content is disposed of: <see cref="ContentRedacted"/> flips,
/// but the fact a message was sent and what became of it remain.
/// </para>
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationType type,
        string toPhoneNumber,
        int? contactNumberId,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        ContactNumberId = contactNumberId;
        IdempotencyKey = idempotencyKey;
        DeliveryStatus = NotificationDeliveryStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The order this message relates to.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity (username/email) of the shopper the message is about. Used for scoping.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination number in E.164. PII — never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The registered contact number this message targeted, if still known.</summary>
    public int? ContactNumberId { get; private set; }

    /// <summary>The provider's identifier for the message (its SID), once the provider accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>
    /// The last known delivery outcome. Holds the provider's own status verbatim once known
    /// (queued/sending/sent/delivered/undelivered/failed/scheduled/canceled), or one of the
    /// app-level sentinels in <see cref="NotificationDeliveryStatus"/> before/if the provider never took it.
    /// </summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The provider's numeric error code when the message failed or was undelivered.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Human-readable provider diagnostic. Never contains the shopper's number.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted) at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator resend; null for order-driven messages.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Record that the provider accepted the message and gave us its SID and initial status.</summary>
    public void RecordAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrWhiteSpace(status) ? NotificationDeliveryStatus.Queued : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
        Touch();
    }

    /// <summary>Record that the message could not be handed to the provider at all.</summary>
    public void RecordSendFailed(string? errorMessage)
    {
        DeliveryStatus = NotificationDeliveryStatus.SendFailed;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from a later read of the provider's message resource.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
            DeliveryStatus = status;
        ErrorCode = errorCode;
        if (!string.IsNullOrWhiteSpace(errorMessage))
            ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Mark a not-yet-sent scheduled message as cancelled at the provider.</summary>
    public void MarkCancelled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Canceled;
        Touch();
    }

    /// <summary>Record that the message content has been disposed of at the provider.</summary>
    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Touch();
    }

    /// <summary>Whether this message reached a terminal state where the shopper did not receive it.</summary>
    public bool IsUndelivered()
        => DeliveryStatus is NotificationDeliveryStatus.Undelivered
            or NotificationDeliveryStatus.Failed
            or NotificationDeliveryStatus.SendFailed;

    /// <summary>Whether this is a scheduled message still awaiting its send time.</summary>
    public bool IsScheduled()
        => DeliveryStatus == NotificationDeliveryStatus.Scheduled && ProviderMessageSid is not null;

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
