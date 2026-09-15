using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) to a shopper about one of their orders.
///
/// It records enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="ProviderStatus"/>) —
/// that a later request can act on the message (re-send it, cancel it, redact it) and report on
/// it, without needing the original request that created it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Provider status assigned locally when a send never reached the provider at all.</summary>
    public const string SendErrorStatus = "send_error";

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
    }

    /// <summary>The shopper this message is about / addressed to (the JWT user name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order this message relates to.</summary>
    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The destination number in E.164. Personal data — never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The text that was sent. Nulled out once the content has been disposed of at the shopper's
    /// request (see <see cref="RedactContent"/>).
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's unique identifier for this message (Twilio Message SID), if assigned.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last delivery outcome the provider reported (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When set, this message is/was scheduled with the provider to go out at this time (the delivery follow-up).</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been disposed of, both here and at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key, when this notification is the product of an operator re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>Records the outcome of a successful call to the provider's send endpoint.</summary>
    public void RecordSent(string providerMessageSid, string? providerStatus, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the message could not be handed to the provider at all (network/API error).</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        ProviderStatus = SendErrorStatus;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the locally-held delivery outcome from the provider.</summary>
    public void UpdateDeliveryState(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    /// <summary>Disposes of the message text locally. The provider-side redaction is done by the caller.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    public void AttachIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// True when the message is known not to have reached the shopper: the provider reported a
    /// terminal failure, the message was cancelled, or it never made it to the provider.
    /// </summary>
    public bool DidNotReachShopper()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return false;
        }

        return ProviderStatus is SendErrorStatus or "failed" or "undelivered" or "canceled";
    }

    /// <summary>True while the message is still scheduled with the provider and can be called off.</summary>
    public bool IsPendingScheduledDelivery()
    {
        return ScheduledSendAt.HasValue &&
               ProviderStatus is "scheduled" or "accepted" or "queued";
    }
}
