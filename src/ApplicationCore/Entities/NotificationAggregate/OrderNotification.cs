using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS eShop sent (or tried to send) about an order. It carries enough of the
/// state the provider owns — the provider's message identifier and the current delivery outcome — that
/// a later request (status refresh, resend, content disposal, reconciliation) can act on it and report
/// on it, not merely the request that first created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local pseudo-status used when the provider never accepted the send (e.g. a transport error),
    /// so there is no provider message and no provider-owned status to show.</summary>
    public const string NotSentStatus = "not_sent";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        Status = NotSentStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper (token identity) who owns the order this message is about.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number (E.164). Personal data — never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Local snapshot of the message text. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message, once the provider has accepted it. Null if the
    /// send was never accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last known delivery outcome. Provider-owned value (queued, sending, sent, delivered,
    /// undelivered, failed, scheduled, canceled, ...) once accepted; <see cref="NotSentStatus"/> otherwise.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>For a scheduled message (the delivery follow-up), when the provider will send it.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this message, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one is a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    /// <summary>Records the provider's acceptance of the send: its message id and the initial status.</summary>
    public void RecordAccepted(string providerMessageSid, string status, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? Status : status;
        ScheduledSendAt = scheduledSendAt;
        ErrorCode = null;
        ErrorMessage = null;
        LastCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider never accepted the send.</summary>
    public void RecordNotSent(string? errorMessage, int? errorCode = null)
    {
        Status = NotSentStatus;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        LastCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the stored delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastCheckedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void SetResendMetadata(int sourceNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Whether the message is currently scheduled with the provider and has not yet gone out.</summary>
    public bool IsPendingScheduled =>
        ProviderMessageSid != null &&
        string.Equals(Status, "scheduled", StringComparison.OrdinalIgnoreCase);
}
