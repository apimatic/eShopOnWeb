using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) about an order. It carries enough of the state
/// the provider owns — the provider's message identifier and current delivery outcome — that a
/// later request can act on it (resend, dispose content, reconcile) and report on it, not only the
/// request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null, int? originalNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        OriginalNotificationId = originalNotificationId;
        DeliveryStatus = NotificationDeliveryStatus.NotSent;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The owning shopper's identity (username), copied from the order for ownership checks.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The message text. Cleared when a shopper asks for the content to be disposed of; the record
    /// (that a message was sent and what became of it) survives.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID), once the provider has accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's raw status string (e.g. queued, delivered, undelivered, scheduled, canceled).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>Normalized delivery outcome derived from <see cref="ProviderStatus"/>.</summary>
    public NotificationDeliveryStatus DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key for the operator re-send that produced this message.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a re-send, the notification it was re-sending.</summary>
    public int? OriginalNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted at the provider and cleared here).</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records that the provider accepted the message and its initial state.</summary>
    public void RecordProviderResult(string providerMessageSid, NotificationDeliveryStatus deliveryStatus,
        string? providerStatus, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Apply(deliveryStatus, providerStatus, errorCode, errorMessage);
    }

    /// <summary>Records that the send could not be attempted with the provider at all.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        DeliveryStatus = NotificationDeliveryStatus.NotSent;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Updates the delivery state from a later provider read.</summary>
    public void UpdateDeliveryState(NotificationDeliveryStatus deliveryStatus, string? providerStatus,
        int? errorCode, string? errorMessage)
    {
        Apply(deliveryStatus, providerStatus, errorCode, errorMessage);
    }

    /// <summary>Disposes of the message content locally. The provider-side redaction is done by the caller.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Apply(NotificationDeliveryStatus deliveryStatus, string? providerStatus, int? errorCode, string? errorMessage)
    {
        DeliveryStatus = deliveryStatus;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
