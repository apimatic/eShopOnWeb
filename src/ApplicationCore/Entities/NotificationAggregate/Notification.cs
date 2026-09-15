using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) to a shopper about one of their orders.
///
/// A notification carries enough of the state the messaging provider owns — the provider's
/// message identifier (<see cref="ProviderMessageSid"/>) and the current delivery outcome
/// (<see cref="DeliveryStatus"/>) — that a later request can act on it (resend, cancel a
/// scheduled follow-up, dispose of its content) and report on it, not only the request that
/// created it.
///
/// The destination number is sensitive and must never be written to logs.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(string buyerId, int orderId, NotificationType type, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        DeliveryStatus = DeliveryStatuses.Pending;
    }

    /// <summary>The shopper this message is about / addressed to (owner of the record).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order this notification relates to.</summary>
    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination number. Sensitive — never log.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once its content has been disposed of (<see cref="ContentRedacted"/>).</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (its SID). Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>
    /// Current delivery outcome. Either the provider's own status wire value
    /// (queued, sent, delivered, undelivered, failed, scheduled, canceled, ...) or an
    /// application sentinel (<see cref="DeliveryStatuses.Pending"/>, <see cref="DeliveryStatuses.SendFailed"/>).
    /// </summary>
    public string DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>The date the provider reports the message was sent, when known.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>True once the message content has been disposed of, at the shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;

    /// <summary>The provider accepted the message. Record its SID and current status/outcome fields.</summary>
    public void RecordAccepted(string? providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrWhiteSpace(status) ? DeliveryStatuses.Unknown : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
    }

    /// <summary>The message could not be handed to the provider at all (a real send failure).</summary>
    public void RecordSendFailure(string? reason)
    {
        DeliveryStatus = DeliveryStatuses.SendFailed;
        ErrorMessage = reason;
    }

    /// <summary>Refresh the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            DeliveryStatus = status;
        }
        // Only overwrite error details when the provider supplies them, so we don't lose a prior reason.
        if (errorCode.HasValue) ErrorCode = errorCode;
        if (!string.IsNullOrWhiteSpace(errorMessage)) ErrorMessage = errorMessage;
        if (dateSent.HasValue) ProviderDateSent = dateSent;
    }

    public void MarkCanceled()
    {
        DeliveryStatus = DeliveryStatuses.Canceled;
    }

    /// <summary>
    /// Dispose of the message content in this application. The provider-side redaction is performed
    /// separately; here we drop the local copy while preserving the fact it was sent and its outcome.
    /// </summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }
}
