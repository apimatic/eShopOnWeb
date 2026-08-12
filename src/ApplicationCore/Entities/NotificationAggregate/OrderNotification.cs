using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or scheduled) about an order.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome
/// (<see cref="DeliveryStatus"/> / <see cref="ProviderErrorCode"/>) — that a later request can
/// act on it (resend, redact, cancel a schedule) and report on it, not only the request that sent it.
///
/// The message text is NOT stored here: it lives with the provider and can be regenerated from
/// <see cref="Kind"/> + order. <see cref="ToPhoneNumber"/> is PII and is never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toPhoneNumber)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        DeliveryStatus = NotificationDeliveryStatus.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>Owning shopper (username / login name). Notifications are scoped to their owner.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number in E.164. PII — never logged.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The provider's message identifier (Twilio Message SID), once the send is accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for this message (or a local marker).</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The provider's error code when the message failed / was undelivered.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>True when this is a follow-up queued with the provider to send at a future time.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider (redacted).</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>True when this notification was produced by an operator re-send.</summary>
    public bool IsResend { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the provider's response to an immediate send.</summary>
    public void RecordSendResult(string? providerMessageSid, string deliveryStatus, int? errorCode)
    {
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        Touch();
    }

    /// <summary>Records that this notification is a follow-up scheduled with the provider.</summary>
    public void MarkScheduled(string? providerMessageSid, string deliveryStatus, DateTimeOffset scheduledFor, int? errorCode)
    {
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        IsScheduled = true;
        ScheduledFor = scheduledFor;
        Touch();
    }

    /// <summary>Updates the delivery outcome from a fresh reading of the provider's record.</summary>
    public void UpdateDeliveryOutcome(string deliveryStatus, int? errorCode)
    {
        DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        Touch();
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Touch();
    }

    public void FlagAsResend(string idempotencyKey)
    {
        IsResend = true;
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
